using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using OpenGptChat.Models;
using OpenGptChat.Services;
using OpenGptChat.ViewModels;

namespace OpenGptChat.Views.Pages
{
    /// <summary>
    /// Interaction logic for ConfigPage.xaml
    /// </summary>
    public partial class ConfigPage : Page
    {
        public ConfigPage(
            AppWindow appWindow,
            ConfigPageModel viewModel,
            PageService pageService,
            NoteService noteService,
            LanguageService languageService,
            ColorModeService colorModeService,
            ConfigurationService configurationService,
            SmoothScrollingService smoothScrollingService,
            ChatService chatService)
        {
            AppWindow = appWindow;
            ViewModel = viewModel;
            PageService = pageService;
            NoteService = noteService;
            LanguageService = languageService;
            ColorModeService = colorModeService;
            ConfigurationService = configurationService;
            ChatService = chatService;
            DataContext = this;

            LoadSystemMessagesCore();

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.SelectedProfile = ConfigurationService.CurrentProfile;

            InitializeComponent();

            smoothScrollingService.Register(configurationScrollViewer);
        }

        public AppWindow AppWindow { get; }
        public ConfigPageModel ViewModel { get; }
        public PageService PageService { get; }
        public NoteService NoteService { get; }
        public LanguageService LanguageService { get; }
        public ColorModeService ColorModeService { get; }
        public ConfigurationService ConfigurationService { get; }
        public ChatService ChatService { get; }

        private ApiProfile? subscribedProfile;
        private CancellationTokenSource? modelsCts;
        private CancellationTokenSource? checkConfigCts;
        private CancellationTokenSource? autoFetchCts;

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedProfile))
            {
                SubscribeProfile(ViewModel.SelectedProfile);

                if (ViewModel.SelectedProfile != null)
                {
                    ConfigurationService.SetActiveProfile(ViewModel.SelectedProfile.Name);
                }
            }
        }

        private void SubscribeProfile(ApiProfile? profile)
        {
            if (subscribedProfile != null)
                subscribedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;

            subscribedProfile = profile;

            if (subscribedProfile != null)
            {
                subscribedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
                _ = ScheduleFetchModelsAsync(subscribedProfile);
            }
        }

        private async void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApiProfile.ApiHost) && sender is ApiProfile profile)
            {
                await ScheduleFetchModelsAsync(profile);
            }
        }

        private void LoadSystemMessagesCore()
        {
            ViewModel.SystemMessages.Clear();
            foreach (var msg in ConfigurationService.Configuration.SystemMessages)
                ViewModel.SystemMessages.Add(new ValueWrapper<string>(msg));
        }

        private void ApplySystemMessagesCore()
        {
            ConfigurationService.Configuration.SystemMessages = ViewModel.SystemMessages
                .Select(wraper => wraper.Value)
                .ToArray();
        }


        [RelayCommand]
        public void GoToMainPage()
        {
            AppWindow.Navigate<MainPage>();
        }

        [RelayCommand]
        public void AboutOpenChat()
        {
            MessageBox.Show(App.Current.MainWindow,
                $"""
                {nameof(OpenGptChat)}, by SlimeNull v{Assembly.GetEntryAssembly()?.GetName()?.Version}

                A simple chat client based on OpenAI Chat completion API.

                Repository: https://github.com/SlimeNull/{nameof(OpenGptChat)}
                """,
                $"About {nameof(OpenGptChat)}", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        public Task LoadSystemMessages()
        {
            LoadSystemMessagesCore();
            return NoteService.ShowAndWaitAsync("System messages loaded", 1500);
        }

        [RelayCommand]
        public Task ApplySystemMessages()
        {
            ApplySystemMessagesCore();
            return NoteService.ShowAndWaitAsync("System messages applied", 1500);
        }

        [RelayCommand]
        public void AddSystemMessage()
        {
            ViewModel.SystemMessages.Add(new ValueWrapper<string>("New system message"));
        }

        [RelayCommand]
        public void RemoveSystemMessage()
        {
            if (ViewModel.SystemMessages.Count > 0)
            {
                ViewModel.SystemMessages.RemoveAt(ViewModel.SystemMessages.Count - 1);
            }
        }

        private async Task ScheduleFetchModelsAsync(ApiProfile profile)
        {
            autoFetchCts?.Cancel();
            autoFetchCts = new CancellationTokenSource();

            try
            {
                await Task.Delay(500, autoFetchCts.Token);
                await FetchModelsAsync(profile, false, autoFetchCts.Token);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            finally
            {
                autoFetchCts = null;
            }
        }

        private async Task FetchModelsAsync(ApiProfile profile, bool notify, CancellationToken token)
        {
            modelsCts?.Cancel();
            modelsCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            ViewModel.IsFetchingModels = true;
            ViewModel.StatusMessage = "正在获取模型...";

            if (string.IsNullOrWhiteSpace(profile.ApiHost))
            {
                ViewModel.StatusMessage = "请先填写 ApiHost";

                if (notify)
                    await NoteService.ShowAndWaitAsync(ViewModel.StatusMessage, 1500);

                ViewModel.IsFetchingModels = false;
                return;
            }

            try
            {
                var models = await ChatService.ListModelsAsync(profile, modelsCts.Token);

                ViewModel.AvailableModels.Clear();
                foreach (var model in models)
                    ViewModel.AvailableModels.Add(model);

                ViewModel.StatusMessage = models.Count > 0 ? $"获取到 {models.Count} 个模型" : "未获取到模型";

                if (notify)
                    await NoteService.ShowAndWaitAsync(ViewModel.StatusMessage, 1500);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (System.Exception ex)
            {
                ViewModel.StatusMessage = $"获取模型失败: {ex.Message}";

                if (notify)
                    await NoteService.ShowAndWaitAsync(ViewModel.StatusMessage, 2000);
            }
            finally
            {
                ViewModel.IsFetchingModels = false;
            }
        }

        [RelayCommand]
        public void AddProfile()
        {
            ApiProfile source = ViewModel.SelectedProfile ?? ConfigurationService.CurrentProfile;

            string baseName = string.IsNullOrWhiteSpace(source.Name) ? "Profile" : source.Name;
            string name = baseName;
            int index = 1;

            while (ConfigurationService.Configuration.ApiProfiles.Any(p => p.Name == name))
            {
                name = $"{baseName}-{index++}";
            }

            ApiProfile profile = new ApiProfile
            {
                Name = name,
                ApiHost = source.ApiHost,
                ApiKey = source.ApiKey,
                Model = source.Model,
                ApiTimeout = source.ApiTimeout,
                Temerature = source.Temerature
            };

            ConfigurationService.Configuration.ApiProfiles.Add(profile);
            ViewModel.SelectedProfile = profile;
        }

        [RelayCommand]
        public async Task RemoveProfile()
        {
            if (ViewModel.SelectedProfile == null)
                return;

            if (ConfigurationService.Configuration.ApiProfiles.Count <= 1)
            {
                await NoteService.ShowAndWaitAsync("At least one profile is required", 1500);
                return;
            }

            ApiProfile toRemove = ViewModel.SelectedProfile;
            ConfigurationService.Configuration.ApiProfiles.Remove(toRemove);

            if (ConfigurationService.Configuration.ActiveApiProfile == toRemove.Name)
                ConfigurationService.Configuration.ActiveApiProfile = ConfigurationService.Configuration.ApiProfiles.First().Name;

            ViewModel.SelectedProfile = ConfigurationService.Configuration.ApiProfiles.FirstOrDefault();
        }

        [RelayCommand]
        public Task RefreshModels()
        {
            if (ViewModel.SelectedProfile == null)
                return Task.CompletedTask;

            return FetchModelsAsync(ViewModel.SelectedProfile, true, CancellationToken.None);
        }

        [RelayCommand]
        public async Task CheckConfiguration()
        {
            if (ViewModel.SelectedProfile == null)
                return;

            checkConfigCts?.Cancel();
            checkConfigCts = new CancellationTokenSource();

            ViewModel.IsCheckingConfig = true;
            ViewModel.StatusMessage = "正在检测配置...";

            try
            {
                var result = await ChatService.ValidateProfileAsync(ViewModel.SelectedProfile, checkConfigCts.Token);

                ViewModel.StatusMessage = result.message;
                await NoteService.ShowAndWaitAsync(result.message, 2000);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (System.Exception ex)
            {
                ViewModel.StatusMessage = $"检测失败: {ex.Message}";
                await NoteService.ShowAndWaitAsync(ViewModel.StatusMessage, 2000);
            }
            finally
            {
                ViewModel.IsCheckingConfig = false;
            }
        }

        [RelayCommand]
        public Task SaveConfiguration()
        {
            ConfigurationService.Configuration.Language =
                LanguageService.CurrentLanguage.ToString();
            ConfigurationService.Configuration.ColorMode =
                ColorModeService.CurrentMode;

            ConfigurationService.SetActiveProfile(ViewModel.SelectedProfile?.Name);
            ConfigurationService.Save();
            return NoteService.ShowAndWaitAsync("Configuration saved", 2000);
        }
    }
}
