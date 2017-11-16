using System;

namespace Samarium.MailProcessor {

    using PluginFramework;
    using PluginFramework.Config;

    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using YamlDotNet;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public sealed class MPConfig: IConfig {

        const string ConfigFilename = "mailproc.yml";
        const string DefaultConfigFilename = "mailproc.def.yml";

        #region Singleton
        private static MPConfig _instance;
        public static MPConfig Instance => _instance;

        public static MPConfig CreateInstance(IConfig systemConfig) => Instance ?? (_instance = new MPConfig(new FileInfo(Path.Combine(systemConfig.GetString("config_dir"), ConfigFilename)), 
                                                                                                new FileInfo(Path.Combine(systemConfig.GetString("config_dir"), DefaultConfigFilename)), 
                                                                                                systemConfig));

        [Obsolete("The parameterless constructor is obsolete and used only for deserialization.")]
        public MPConfig() { }

        private MPConfig(FileInfo cfgFile, FileInfo defCfgFile, IConfig systemCfg) {
            this.cfgFile = cfgFile;
            this.defCfgFile = defCfgFile;
            this.systemConfig = systemCfg;

            if (cfgFile.Exists)
                LoadConfigs();
            else
                LoadDefaults();
        }
        #endregion

        #region Actual Configurations
        [YamlMember(Alias = "search_interval", ApplyNamingConventions = true)]
        public TimeSpan SearchInterval { get; set; } = new TimeSpan(0, 10, 0);

        [YamlMember(Alias = "max_emails", ApplyNamingConventions = true)]
        public int MaxEmailsPerRun { get; set; } = 6000; // Nobody should even reach this number...

        [YamlMember(Alias = "use_multithreading", ApplyNamingConventions = true)]
        public bool UseMultithreading { get; set; } = true;

        [YamlMember(Alias = "core_use", ApplyNamingConventions = true)]
        public short CoreUse { get; set; } = -1; // The number of cores to use. -1 = self-determine

        [YamlMember(Alias = "pre_index_file_ext", ApplyNamingConventions = true)]
        public string PreIndexFext { get; set; } = ".noidx"; // Not yet indexed

        [YamlMember(Alias = "post_index_file_ext", ApplyNamingConventions = true)]
        public string PostIndexFext { get; set; } = ".eml"; // Typical MIME format

        [YamlMember(Alias = "search_directories", ApplyNamingConventions = true)]
        public List<string> SearchDirectories { get; set; } = new List<string>();

        [YamlMember(Alias = "domain_regexp", ApplyNamingConventions = true)]
        public string DomainPattern { get; set; } = @"^(((?!-))(xn--)?[a-z0-9-_]{0,61}[a-z0-9]{1,1}\.)*(xn--)?([a-z0-9\-]{1,61}|[a-z0-9-]{1,30}\.[a-z]{2,})$"; // Thanks to timgws@stackoverflow!

        [YamlMember(Alias = "username_regexp", ApplyNamingConventions = true)]
        public string UsernamePattern { get; set; } = @"[a-zA-Z0-9.!#$%&’*+/=?^_`{|}~-]+"; // Regex used by input type="email"

        [YamlMember(Alias = "archive_root", ApplyNamingConventions = true)]
        public string ArchiveRootPath { get; set; } = "./container/";
        #endregion

        #region Variables
        FileInfo cfgFile;
        FileInfo defCfgFile;
        IConfig systemConfig;
        #endregion

        [YamlIgnore]
        public bool IsDynamic => false;

        [YamlIgnore]
        public int ConfigCount => typeof(MPConfig).GetProperties().Count(x => x.GetCustomAttributes(false).OfType<YamlMemberAttribute>().FirstOrDefault() != null);

        [YamlIgnore]
        public List<string> Keys {
            get {
                var attributes =
                    from property in typeof(MPConfig).GetProperties()
                    select property.GetCustomAttributes(false).OfType<YamlMemberAttribute>().FirstOrDefault();
                return
                    (
                        from attr in attributes
                        select attr.Alias
                    ).Concat(
                        from property in typeof(MPConfig).GetProperties()
                        where property.GetCustomAttributes(false).OfType<YamlMemberAttribute>().FirstOrDefault() != null
                        select property.Name
                    ).ToList();
                    
            }
        }

        [YamlIgnore]
        public string Name { get; }

        public event ConfigSetEventHandler ConfigSet;
        public event ConfigsLoadedEventHandler ConfigsLoaded;

        public bool GetBool(string key) => GetConfig<bool>(key);

        public T GetConfig<T>(string key) {
            var prop = GetType().GetProperties()
                                .FirstOrDefault(x => {

                         var yamlAttr = x.GetCustomAttributes(false).OfType<YamlMemberAttribute>().FirstOrDefault();

                         if (yamlAttr is default)
                             return false;

                         return x.Name == key || yamlAttr.Alias == key;
                     });

            if (prop.GetValue(this) is T tVal)
                return tVal;
            else
                throw new InvalidCastException(string.Format("Cannot cast type {0} to {1}!", typeof(T).Name, prop.GetValue(this).GetType().Name));
        }

        public bool TryGetConfig<T>(string key, out T cfg) {
            try {
                cfg = GetConfig<T>(key);
                return true;
            } catch {
                cfg = default;
                return false;
            }
        }

        public double GetDouble(string key) => GetConfig<double>(key);
        public int GetInt(string key) => GetConfig<int>(key);
        public string GetString(string key) => GetConfig<string>(key);
        public bool HasKey(string key) => Keys.Contains(key);

        public void LoadConfigs() {
            using (var stream = cfgFile.OpenText()) {
                var deserializedConfig = new DeserializerBuilder().WithNamingConvention(new UnderscoredNamingConvention()).Build().Deserialize<MPConfig>(stream);
                SearchInterval = deserializedConfig.SearchInterval;
                MaxEmailsPerRun = deserializedConfig.MaxEmailsPerRun;
                UseMultithreading = deserializedConfig.UseMultithreading;
                CoreUse = deserializedConfig.CoreUse;
                PreIndexFext = deserializedConfig.PreIndexFext;
                PostIndexFext = deserializedConfig.PostIndexFext;
                SearchDirectories = deserializedConfig.SearchDirectories;
                DomainPattern = deserializedConfig.DomainPattern;
                UsernamePattern = deserializedConfig.UsernamePattern;
            }
            ConfigsLoaded?.Invoke(this);
        }

        public void LoadDefaults() {
            if (!defCfgFile.Exists) {
                using (var cfgFile = defCfgFile.Create())
                using (var resourceStream = GetType().Assembly.GetManifestResourceStream("Samarium.MailProcessor.Resources.ConfigDefaults.mailproc.yml")) {
                    resourceStream.CopyTo(cfgFile);
                }
            }

            using (var stream = defCfgFile.OpenText()) {
                var deserializedConfig = new DeserializerBuilder().WithNamingConvention(new UnderscoredNamingConvention()).Build().Deserialize<MPConfig>(stream);
                SearchInterval = deserializedConfig.SearchInterval;
                MaxEmailsPerRun = deserializedConfig.MaxEmailsPerRun;
                UseMultithreading = deserializedConfig.UseMultithreading;
                CoreUse = deserializedConfig.CoreUse;
                PreIndexFext = deserializedConfig.PreIndexFext;
                PostIndexFext = deserializedConfig.PostIndexFext;
                SearchDirectories = deserializedConfig.SearchDirectories;
                DomainPattern = deserializedConfig.DomainPattern;
                UsernamePattern = deserializedConfig.UsernamePattern;
            }
            ConfigsLoaded?.Invoke(this);
        }

        public void SaveConfigs() {
            File.WriteAllText(cfgFile.FullName, new SerializerBuilder().WithNamingConvention(new UnderscoredNamingConvention()).Build().Serialize(this));
        }

        public void SetConfig<T>(string key, T value) {
            var prop = (from property in GetType().GetProperties()
                        where property.Name == key || ((property.GetCustomAttributes(false).OfType<YamlMemberAttribute>().FirstOrDefault())?.Alias == key)
                        select property).FirstOrDefault(); // I like to do it this way sometimes. Don't judge.

            prop.SetValue(this, value);
            ConfigSet?.Invoke(this, key);
        }

        public string ToString(ConfigSerializationType serializationType = ConfigSerializationType.Yaml) => throw new NotImplementedException();
        public IEnumerable<T> Where<T>(Func<string, bool> predicate) => throw new NotImplementedException();
    }
}
