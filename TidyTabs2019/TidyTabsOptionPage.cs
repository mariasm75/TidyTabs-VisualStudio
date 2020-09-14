// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TidyTabsOptionPage.cs" company="Dave McKeown">
//   Apache 2.0 License
// </copyright>
// <summary>
//   TidyTabsOptionPage provides the ability to manage settings for TidyTabs
// </summary>
// --------------------------------------------------------------------------------------------------------------------
namespace DaveMcKeown.TidyTabs
{
    using DaveMcKeown.TidyTabs.Properties;

    using Microsoft.VisualStudio.Shell;
    using System.ComponentModel;

    /// <summary>
    ///     TidyTabsOptionPage provides the ability to manage settings for TidyTabs
    /// </summary>
    public class TidyTabsOptionPage : DialogPage
    {
        /// <summary>
        ///     Tidy Tabs settings
        /// </summary>
        private Settings settings;

        /// <summary>
        ///     Gets or sets a value indicating whether tabs should be purged when a file is saved
        /// </summary>
        [Category("Behavior")]
        [DisplayName("Close on save")]
        [Description("Controls if stale tabs should be automatically closed when saving a document")]
        public bool PurgeTabsOnSave
        {
            get
            {
                return Settings.PurgeStaleTabsOnSave;
            }

            set
            {
                Settings.PurgeStaleTabsOnSave = value;
                Settings.Save();
            }
        }

        /// <summary>
        ///     Gets or sets the timeout for tabs before they are marked as stale
        /// </summary>
        [Category("Settings")]
        [DisplayName("Timeout Minutes")]
        [Description("The time-out before a tab becomes inactive if not viewed or modified")]
        public int TabTimeoutMinutes
        {
            get
            {
                return Settings.TabTimeoutMinutes;
            }

            set
            {
                Settings.TabTimeoutMinutes = value;
                Settings.Save();
            }
        }

        /// <summary>
        ///     Gets or sets the threshold for number of open tabs at which point to start closing tabs
        /// </summary>
        [Category("OptionPage")]
        [DisplayName("Open Tab Threshold")]
        [Description("The number tabs that must be open before inactive tabs begin to be closed by Tidy Tabs. Set to 0 to always close inactive tabs")]
        public int TabCloseThreshold
        {
            get
            {
                return Settings.TabCloseThreshold;
            }

            set
            {
                Settings.TabCloseThreshold = value;
                Settings.Save();
            }
        }

        /// <summary>
        ///     Gets or sets the maximum number of saved open document tabs
        /// </summary>
        [Category("Settings")]
        [DisplayName("Maximum Open Tabs")]
        [Description("The maximum number of tabs that should be open. The oldest tabs will be closed until this number is met, even if they do not exceed the time-out value")]
        public int MaxOpenTabs
        {
            get
            {
                return Settings.MaxOpenTabs;
            }

            set
            {
                Settings.MaxOpenTabs = value;
                Settings.Save();
            }
        }

        /// <summary>
        ///     Gets the Settings
        /// </summary>
        private Settings Settings
        {
            get
            {
                return settings ?? (settings = SettingsProvider.Instance);
            }
        }
    }
}