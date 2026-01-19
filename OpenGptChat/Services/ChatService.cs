using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OpenGptChat.Models;

namespace OpenGptChat.Services
{
    public class ChatService
    {
        public ChatService(
            ChatStorageService chatStorageService,
            ConfigurationService configurationService)
        {
            ChatStorageService = chatStorageService;
            ConfigurationService = configurationService;
        }

        private string? client_apikey;
        private string? client_apihost;

        public ChatStorageService ChatStorageService { get; }

        public ConfigurationService ConfigurationService { get; }

        private string GetCompletionUrl(ApiProfile profile)
        {
            string host = profile.ApiHost.Trim().TrimEnd('/');
            
            if (!host.StartsWith("http://") && !host.StartsWith("https://"))
                host = $"https://{host}";
            
            if (!host.EndsWith("/v1"))
                host = $"{host}/v1";

            return $"{host}/chat/completions";
        }

        public async Task<IReadOnlyList<string>> ListModelsAsync(ApiProfile profile, CancellationToken token)
        {
            // Simple implementation for ListModels using HttpClient
            try 
            {
                using HttpClient client = new HttpClient();
                string url = GetCompletionUrl(profile).Replace("/chat/completions", "/models"); 
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {profile.ApiKey}");

                var response = await client.SendAsync(request, token);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync(token);
                var root = JsonNode.Parse(jsonString);
                
                var models = new List<string>();
                if (root?["data"] is JsonArray data)
                {
                    foreach (var item in data)
                    {
                        var id = item?["id"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(id))
                            models.Add(id);
                    }
                }
                return models;
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<(bool success, string message)> ValidateProfileAsync(ApiProfile profile, CancellationToken token)
        {
            try
            {
                var models = await ListModelsAsync(profile, token);
                if (models.Count > 0)
                    return (true, "配置可用，已获取模型列表");

                return (true, "配置可用，但未能获取模型列表");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        CancellationTokenSource? cancellation;

        public ChatSession NewSession(string name)
        {
            ChatSession session = ChatSession.Create(name);
            ChatStorageService.SaveSession(session);

            return session;
        }

        public Task<ChatDialogue> ChatAsync(Guid sessionId, string message, Action<string> messageHandler)
        {
            cancellation?.Cancel();
            cancellation = new CancellationTokenSource();

            return ChatCoreAsync(sessionId, message, messageHandler, cancellation.Token);
        }

        public Task<ChatDialogue> ChatAsync(Guid sessionId, string message, Action<string> messageHandler, CancellationToken token)
        {
            cancellation?.Cancel();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

            return ChatCoreAsync(sessionId, message, messageHandler, cancellation.Token);
        }

        public Task<string> GetTitleAsync(Guid sessionId, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public void Cancel()
        {
            cancellation?.Cancel();
        }

        private async Task<ChatDialogue> ChatCoreAsync(Guid sessionId, string message, Action<string> messageHandler, CancellationToken token)
        {
            ChatSession? session = 
                ChatStorageService.GetSession(sessionId);

            ApiProfile profile = ConfigurationService.CurrentProfile;

            ChatMessage ask = ChatMessage.Create(sessionId, "user", message);

            var messages = new List<object>();

            foreach (var sysmsg in ConfigurationService.Configuration.SystemMessages)
                messages.Add(new { role = "system", content = sysmsg });

            if (session != null)
                foreach (var sysmsg in session.SystemMessages)
                    messages.Add(new { role = "system", content = sysmsg });

            foreach (var chatmsg in ChatStorageService.GetAllMessages(sessionId))
                messages.Add(new { role = chatmsg.Role, content = chatmsg.Content });

            messages.Add(new { role = "user", content = message });

            string modelName =
                profile.Model;
            double temperature =
                profile.Temerature;

            DateTime lastTime = DateTime.Now;

            StringBuilder sb = new StringBuilder();

            using HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromHours(1); // Long timeout for streaming

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                stream = true,
                temperature = temperature
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GetCompletionUrl(profile));
            request.Headers.Add("Authorization", $"Bearer {profile.ApiKey}");
            request.Content = new StringContent( JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            CancellationTokenSource completionTaskCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token);

            Task completionTask = Task.Run(async () =>
            {
                try 
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, completionTaskCancellation.Token);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(completionTaskCancellation.Token);
                    using var reader = new System.IO.StreamReader(stream);

                    string? line;
                    bool isThinking = false;
                    bool hasEmittedThinking = false;

                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (completionTaskCancellation.IsCancellationRequested)
                            break;

                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.StartsWith("data: "))
                        {
                            var json = line.Substring(6).Trim();
                            if (json == "[DONE]") break;

                            try
                            {
                                var node = JsonNode.Parse(json);
                                var choice = node?["choices"]?[0];
                                var delta = choice?["delta"];

                                if (delta != null)
                                {
                                    // 1. Handle reasoning content
                                    var reasoning = delta["reasoning_content"]?.GetValue<string>();
                                    // Some models use 'reasoning' instead of 'reasoning_content', check both if necessary.
                                    // DeepSeek R1 uses 'reasoning_content'.
                                    
                                    if (!string.IsNullOrEmpty(reasoning))
                                    {
                                        if (!isThinking) 
                                        {
                                            // Start of thinking block
                                            if (!hasEmittedThinking)
                                            {
                                                sb.Append("::: think\n");
                                                hasEmittedThinking = true;
                                            }
                                            isThinking = true;
                                        }

                                        sb.Append(reasoning);
                                        messageHandler.Invoke(sb.ToString());
                                        lastTime = DateTime.Now;
                                    }

                                    // 2. Handle standard content
                                    var content = delta["content"]?.GetValue<string>();
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        if (isThinking)
                                        {
                                            // End of thinking block
                                            sb.Append("\n:::\n");
                                            isThinking = false;
                                        }

                                        sb.Append(content);
                                        messageHandler.Invoke(sb.ToString());
                                        lastTime = DateTime.Now;
                                    }
                                }
                            }
                            catch { /* Ignore parse errors */ }
                        }
                    }
                    
                    if (isThinking)
                    {
                         sb.Append("\n:::\n");
                         messageHandler.Invoke(sb.ToString());
                    }
                }
                catch (Exception ex)
                {
                    // If cancellation was requested, it's fine
                    if (!completionTaskCancellation.IsCancellationRequested)
                    {
                         // Handle error or rethrow
                         // For now, let the timeout handler manage it or just log
                    }
                }
            }, completionTaskCancellation.Token);

            // Timeout Watchdog
            Task cancelTask = Task.Run(async () =>
            {
                try
                {
                    TimeSpan timeout = 
                        TimeSpan.FromMilliseconds(profile.ApiTimeout);

                    while (!completionTask.IsCompleted)
                    {
                        await Task.Delay(100);

                        if ((DateTime.Now - lastTime) > timeout)
                        {
                            completionTaskCancellation.Cancel();
                            throw new TimeoutException();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            });

            await Task.WhenAll(completionTask, cancelTask);

            ChatMessage answer = ChatMessage.Create(sessionId, "assistant", sb.ToString());
            ChatDialogue dialogue = new ChatDialogue(ask, answer);

            ChatStorageService.SaveMessage(ask);
            ChatStorageService.SaveMessage(answer);

            return dialogue;
        }
    }
}
