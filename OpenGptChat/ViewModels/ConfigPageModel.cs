using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenGptChat.Models;

namespace OpenGptChat.ViewModels
{
    public partial class ConfigPageModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<ValueWrapper<string>> _systemMessages =
            new ObservableCollection<ValueWrapper<string>>();

        [ObservableProperty]
        private ApiProfile? _selectedProfile;

        [ObservableProperty]
        private ObservableCollection<string> _availableModels = new();

        [ObservableProperty]
        private bool _isCheckingConfig;

        [ObservableProperty]
        private bool _isFetchingModels;

        [ObservableProperty]
        private string _statusMessage = string.Empty;
    }
}
