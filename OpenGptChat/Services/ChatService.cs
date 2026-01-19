using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
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

        private OpenAIClient? client;
        private string? client_apikey;
        private string? client_apihost;

        public ChatStorageService ChatStorageService { get; }

        public ConfigurationService ConfigurationService { get; }

        private void NewOpenAIClient(
            [NotNull] out OpenAIClient client, 
            [NotNull] out string client_apikey,
            [NotNull] out string client_apihost)
        {
            ApiProfile profile = ConfigurationService.CurrentProfile;

            client_apikey = profile.ApiKey;
            client_apihost = profile.ApiHost;

            client = CreateOpenAIClient(profile);
        }

        private OpenAIClient GetOpenAIClient()
        {
            ApiProfile profile = ConfigurationService.CurrentProfile;

            if (client == null ||
                client_apikey != profile.ApiKey ||
                client_apihost != profile.ApiHost)
                NewOpenAIClient(out client, out client_apikey, out client_apihost);

            return client;
        }

        private string NormalizeHost(string apiHost)
        {
            if (string.IsNullOrWhiteSpace(apiHost))
                return string.Empty;

            string host = apiHost.Trim();

            if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(8);
            else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(7);

            host = host.TrimEnd('/');

            if (host.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(0, host.Length - 3).TrimEnd('/');

            return host;
        }

        private OpenAIClient CreateOpenAIClient(ApiProfile profile)
        {
            string host = NormalizeHost(profile.ApiHost);

            return new OpenAIClient(
                new OpenAIAuthentication(profile.ApiKey),
                new OpenAISettings(domain: host)); // Updated to OpenAISettings
        }

        public async Task<IReadOnlyList<string>> ListModelsAsync(ApiProfile profile, CancellationToken token)
        {
            OpenAIClient tempClient = CreateOpenAIClient(profile);
            var models = await tempClient.ModelsEndpoint.GetModelsAsync(token);

            return models
                .Select(m => m.Id)
                .ToList();
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

            OpenAIClient client = GetOpenAIClient();

            List<Message> messages = new List<Message>();

            foreach (var sysmsg in ConfigurationService.Configuration.SystemMessages)
                messages.Add(new Message(Role.System, sysmsg));

            if (session != null)
                foreach (var sysmsg in session.SystemMessages)
                    messages.Add(new Message(Role.System, sysmsg));

            foreach (var chatmsg in ChatStorageService.GetAllMessages(sessionId))
                messages.Add(new Message(Enum.Parse<Role>(chatmsg.Role, true), chatmsg.Content));

            messages.Add(new Message(Role.User, message));

            string modelName =
                profile.Model;
            double temperature =
                profile.Temerature;

            DateTime lastTime = DateTime.Now;

            StringBuilder sb = new StringBuilder();

            CancellationTokenSource completionTaskCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token);

            Task completionTask = client.ChatEndpoint.StreamCompletionAsync(
                new ChatRequest(messages, modelName, temperature),
                async response =>
                {
                    string? content = response.Choices.FirstOrDefault()?.Delta?.Content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        sb.Append(content);

                        while (sb.Length > 0 && char.IsWhiteSpace(sb[0]))
                            sb.Remove(0, 1);

                        messageHandler.Invoke(sb.ToString());

                        // 有响应了, 更新时间
                        lastTime = DateTime.Now;
                    }
                    
                    await Task.CompletedTask;
                },
                true,
                completionTaskCancellation.Token);

            Task cancelTask = Task.Run(async () =>
            {
                try
                {
                    TimeSpan timeout = 
                        TimeSpan.FromMilliseconds(profile.ApiTimeout);

                    while (!completionTask.IsCompleted)
                    {
                        await Task.Delay(100);

                        // 如果当前时间与上次响应的时间相差超过配置的超时时间, 则扔异常
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
