using System;

namespace Samarium.MailProcessor {

    using PluginFramework;
    using PluginFramework.Command;
    using PluginFramework.Config;
    using PluginFramework.Plugin;

    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public partial class MailProcessor : Plugin {

        #region IPlugin
        public override string PluginName => nameof(MailProcessor);

        private List<ICommand> _pluginCommands;
        public override List<ICommand> PluginCommands { get => _pluginCommands; }

        private IConfig _pluginConfig;
        protected override IConfig PluginConfig { get => _pluginConfig; }

        public override void OnLoaded() {  }

        public override bool OnStart() {
            Info("Initializing configuration...");
            _pluginConfig = MPConfig.CreateInstance(SystemConfig);

            Info("Initializing plugin commands...");
            _pluginCommands = new List<ICommand> {
                #region mp-cfg
                new Command {
                    Arguments = new[] { "--load", "--load-def" },
                    CommandTag = "mp-cfg",
                    Description =
                        "Manages configs for this plugin.\n" +
                        "This plugin's configurations can only be set via the config file\n" +
                        "and then loaded via this command.\n\n" +
                        "Usage:\n" +
                        "\tmp-cfg <arg>\n\n" +
                        "Description:\n" +
                        "\tArguments:\n" +
                        "\t\t--load\t\tLoads the configurations from disk.\n" +
                        "\t\t--load-def\tLoads the default configurations from disk.\n",
                    Handler = Command_MpConfig,
                    ParentPlugin = this,
                    ShortDescription = "Basic plugin config management.",
                },
                #endregion
                #region mp-start
                new Command {
                    Arguments = new[] { "--dry", "--run-on-mt" },
                    CommandTag = "mp-start",
                    Description = 
                        "Starts the subprocess responsible for processing emails,\n" +
                        "indexing, and archiving them.\n" +
                        "Certain options can be overriden using this command.\n" +
                        "Please not that overriding options WILL temporarily store them in\n" +
                        "the configuration; saving configs will STORE these configurations\n" +
                        "permanently!\n\n" +
                        "Usage:\n" +
                        "\tmp-start [arguments] [switches]\n\n" +
                        "Description:\n" +
                        "\tArguments:\n" +
                        "\t\t--dry\t\t\tPerform a single dry-run. No files will be affected.\n" +
                        "\t\t--run-on-mt\t\tForce execution on the main application thread.\n" +
                        "\tSwitches:\n" +
                        "\t\t-core-use=\t\tOverrides the number of cores to use.\n" +
                        "\t\t-max-emails=\t\tOverrides the max. no. of emails to process.\n",
                    Handler = Command_MpStart,
                    ParentPlugin = this,
                    ShortDescription = "Starts the processing subprocess.",
                    Switches = new Dictionary<string, string[]> {
                        { "-core-use=", default },
                        { "-max-emails=", default }
                    }
                },
                #endregion
                #region mp-stop
                new Command {
                    CommandTag = "mp-stop",
                    Description =
                        "Stops the subprocess responsible for processing emails.\n" +
                        "Once stopped, this process will not start until called by\n" +
                        "the 'mp-start' command.",
                    Handler = Command_MpStop,
                    ParentPlugin = this,
                    ShortDescription = "Stops the processing subprocess."
                }
                #endregion
            };

            // TEST
            //var uniqueID = Extensions.GenerateUniqueId();
            //ProcessorLooper(null);

            return true;
        }
        public override bool OnStop() => true; // TODO
        #endregion

    }
}
