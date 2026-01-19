using System.ComponentModel;
using System.Linq;
using System.Reflection;
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
            SmoothScrollingService smoothScrollingService)
        {
            AppWindow = appWindow;
            ViewModel = viewModel;
            PageService = pageService;
            NoteService = noteService;
            LanguageService = languageService;
            ColorModeService = colorModeService;
            ConfigurationService = configurationService;
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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedProfile) && ViewModel.SelectedProfile != null)
            {
                ConfigurationService.SetActiveProfile(ViewModel.SelectedProfile.Name);
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
