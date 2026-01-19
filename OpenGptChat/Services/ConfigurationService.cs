using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenGptChat.Models;
using OpenGptChat.Utilities;

namespace OpenGptChat.Services
{
    public class ConfigurationService
    {
        public ConfigurationService(IOptions<AppConfig> configuration)
        {
            OptionalConfiguration = configuration;

            EnsureProfiles();
        }

        private IOptions<AppConfig> OptionalConfiguration { get; }

        public AppConfig Configuration => OptionalConfiguration.Value;

        public ApiProfile CurrentProfile
        {
            get
            {
                EnsureProfiles();

                var profile = Configuration.ApiProfiles
                    .FirstOrDefault(p => p.Name == Configuration.ActiveApiProfile)
                    ?? Configuration.ApiProfiles.First();

                if (Configuration.ActiveApiProfile != profile.Name)
                    Configuration.ActiveApiProfile = profile.Name;

                return profile;
            }
        }

        public void SetActiveProfile(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                return;

            EnsureProfiles();

            if (Configuration.ApiProfiles.Any(p => p.Name == profileName))
                Configuration.ActiveApiProfile = profileName;
        }

        private void EnsureProfiles()
        {
            // Ensure collection initialized
            Configuration.ApiProfiles ??= new System.Collections.ObjectModel.ObservableCollection<ApiProfile>();

            // Migrate legacy single-config fields into a default profile if none exists
            if (Configuration.ApiProfiles.Count == 0)
            {
                Configuration.ApiProfiles.Add(new ApiProfile
                {
                    Name = string.IsNullOrWhiteSpace(Configuration.ActiveApiProfile) ? "Default" : Configuration.ActiveApiProfile,
                    ApiHost = Configuration.ApiHost,
                    ApiKey = Configuration.ApiKey,
                    Organization = Configuration.Organization,
                    Model = Configuration.Model,
                    ApiTimeout = Configuration.ApiTimeout,
                    Temerature = Configuration.Temerature
                });
            }

            // Ensure active profile exists
            if (string.IsNullOrWhiteSpace(Configuration.ActiveApiProfile) ||
                !Configuration.ApiProfiles.Any(p => p.Name == Configuration.ActiveApiProfile))
            {
                Configuration.ActiveApiProfile = Configuration.ApiProfiles.First().Name;
            }
        }

        public void Save()
        {
            EnsureProfiles();
            using FileStream fs = File.Create(GlobalValues.JsonConfigurationFilePath);
            JsonSerializer.Serialize(fs, OptionalConfiguration.Value, JsonHelper.ConfigurationOptions);
        }
    }
}
