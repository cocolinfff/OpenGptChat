using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.Input;
using OpenGptChat.Models;
using OpenGptChat.Services;
using OpenGptChat.ViewModels;

namespace OpenGptChat.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ChatSessionConfigDialog.xaml
    /// </summary>
    public partial class ChatSessionConfigDialog : Window
    {
        public ChatSessionConfigDialog(ChatSessionModel session)
        {
            Session = session;
            DataContext = this;

            NoteService =
                App.GetService<NoteService>();

            InitializeComponent();
        }

        public ChatSessionModel Session { get; }
        public NoteService NoteService { get; }


        [RelayCommand]
        public void AddSystemMessage()
        {
            Session.SystemMessages.Add(new ValueWrapper<string>("New system message"));
        }

        [RelayCommand]
        public void RemoveSystemMessage()
        {
            if (Session.SystemMessages.Count > 0)
            {
                Session.SystemMessages.RemoveAt(Session.SystemMessages.Count - 1);
            }
        }

        [RelayCommand]
        public void Accept()
        {
            DialogResult = true;

            Close();
        }
    }
}
