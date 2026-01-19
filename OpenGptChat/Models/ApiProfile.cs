using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenGptChat.Models
{
    public partial class ApiProfile : ObservableObject
    {
        [ObservableProperty]
        private string _name = "Default";

        [ObservableProperty]
        private string _apiHost = "openaiapi.elecho.org";

        [ObservableProperty]
        private string _apiKey = string.Empty;

        [ObservableProperty]
        private string _model = "gpt-3.5-turbo";

        [ObservableProperty]
        private int _apiTimeout = 5000;

        [ObservableProperty]
        private double _temerature = .5;

        [ObservableProperty]
        private bool _thinking = false;
    }
}
