﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DaveMcKeown.TidyTabs;
using DaveMcKeown.TidyTabs.Properties;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace TidyTabs2019
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(TidyTabs2019Package.PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionOpening_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(TidyTabsOptionPage), "Tidy Tabs", "Options", 1000, 1001, false)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class TidyTabs2019Package : AsyncPackage, IVsBroadcastMessageEvents, IDisposable
    {


        /// <summary>
        /// TidyTabs2019Package GUID string.
        /// </summary>
        public const string PackageGuidString = "9d06e42b-1fc2-49ec-b76b-13a5411be4cb";
        public static readonly Guid CommandSet = new Guid("f2f30c6f-91e8-413b-bdb5-48db29d13c9f");

        /// <summary>
        ///     A lock object for document purge operations
        /// </summary>
        private readonly object documentPurgeLock = new object();

        /// <summary>
        ///     Dictionary that tracks window hash codes and when they were last seen
        /// </summary>
        private readonly ConcurrentDictionary<Window, DateTime> documentLastSeen = new ConcurrentDictionary<Window, DateTime>();

        /// <summary>
        ///     Visual studio build events
        /// </summary>
        private BuildEvents buildEvents;

        /// <summary>
        ///     Disposed state
        /// </summary>
        private bool disposed;

        /// <summary>
        ///     Visual studio document events
        /// </summary>
        private DocumentEvents documentEvents;

        /// <summary>
        ///     The time of the last text editor action
        /// </summary>
        private DateTime lastAction = DateTime.MinValue;

        /// <summary>
        ///     Backing field for the service provider
        /// </summary>
        private ServiceProvider provider;

        /// <summary>
        ///     The visual studio shell reference
        /// </summary>
        private IVsShell shell;

        /// <summary>
        ///     Shell cookie from message subscription
        /// </summary>
        private uint shellCookie;

        /// <summary>
        ///     Visual studio solution events
        /// </summary>
        private SolutionEvents solutionEvents;

        /// <summary>
        ///     Visual studio text editor events
        /// </summary>
        private TextEditorEvents textEditorEvents;

        /// <summary>
        ///     The DTE COM object for the visual studio automation object
        /// </summary>
        private DTE2 visualStudio;

        /// <summary>
        ///     Visual studio window events
        /// </summary>
        private WindowEvents windowEvents;

        /// <summary>
        ///     Gets the Visual Studio service provider
        /// </summary>
        public IServiceProvider Provider
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return provider ?? (provider = new ServiceProvider(VisualStudio.DTE as Microsoft.VisualStudio.OLE.Interop.IServiceProvider));
            }
        }


        /// <summary>
        ///     Gets the Visual Studio DTE COM Object
        /// </summary>
        public DTE2 VisualStudio
        {
            get
            {
                return visualStudio ?? (visualStudio = GetGlobalService(typeof(SDTE)) as DTE2);
            }
        }

        /// <summary>
        ///     Gets the settings for Tidy Tabs
        /// </summary>
        internal Settings Settings
        {
            get
            {
                return SettingsProvider.Instance;
            }
        }

        /// <summary>
        ///     Implements IDisposable
        /// </summary>
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!disposed)
            {
                if (provider != null)
                {
                    provider.Dispose();
                }

                if (shell != null)
                {
                    shell.UnadviseBroadcastMessages(shellCookie);
                }

                disposed = true;
            }
        }

        /// <summary>Implements the IVsBroadcastMessageEvents interface</summary>
        /// <param name="msg">The notification message</param>
        /// <param name="wordParam">Word value parameter</param>
        /// <param name="longParam">Long integer parameter</param>
        /// <returns>S_OK on success</returns>
        public int OnBroadcastMessage(uint msg, IntPtr wordParam, IntPtr longParam)
        {
            // WM_ACTIVATEAPP 
            if (msg != 0x1C)
            {
                return VSConstants.S_OK;
            }

            lock (documentPurgeLock)
            {
                if (lastAction != DateTime.MinValue)
                {
                    var idleTime = DateTime.Now - lastAction;

                    //foreach (var windowTimePair in documentLastSeen)
                    //{
                    //    UpdateWindowTimestamp(windowTimePair.Key, windowTimePair.Value.Add(idleTime));
                    //}
                }
            }

            lastAction = DateTime.Now;

            return VSConstants.S_OK;
        }


        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            shell = (IVsShell)await GetServiceAsync(typeof(SVsShell));
            windowEvents = VisualStudio.Events.WindowEvents;
            documentEvents = VisualStudio.Events.DocumentEvents;
            textEditorEvents = VisualStudio.Events.TextEditorEvents;
            solutionEvents = VisualStudio.Events.SolutionEvents;
            buildEvents = VisualStudio.Events.BuildEvents;

            windowEvents.WindowActivated += WindowEventsWindowActivated;
            documentEvents.DocumentClosing += DocumentEventsOnDocumentClosing;
            documentEvents.DocumentSaved += DocumentEventsOnDocumentSaved;
            textEditorEvents.LineChanged += TextEditorEventsOnLineChanged;
            solutionEvents.Opened += SolutionEventsOnOpened;
            buildEvents.OnBuildBegin += BuildEventsOnOnBuildBegin;


            if (shell != null)
            {
                shell.AdviseBroadcastMessages(this, out shellCookie);
            }

            OleMenuCommandService menuCommandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            if (menuCommandService != null)
            {
                CommandID menuCommandId = new CommandID(CommandSet, (int)0x0100);
                MenuCommand menuItem = new MenuCommand(TidyTabsMenuItemCommandActivated, menuCommandId);
                menuCommandService.AddCommand(menuItem);
            }
            InitWithOpenWindows();
        }

        /// <summary>Closes stale windows when a build is triggered</summary>
        /// <param name="scope">The build scope</param>
        /// <param name="action">The build action</param>
        private void BuildEventsOnOnBuildBegin(vsBuildScope scope, vsBuildAction action)
        {
            try
            {
                lastAction = DateTime.Now;
                TidyTabs(true);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        /// <summary>The cleanup action for Tidy Tabs that closes stale windows and document windows beyond the max open
        ///     window threshold</summary>
        /// <param name="autoSaveTriggered">Flag that indicates if the action was triggered by a document save event</param>
        private void TidyTabs(bool autoSaveTriggered)
        {
            if (autoSaveTriggered && !Settings.PurgeStaleTabsOnSave)
            {
                return;
            }

            lock (documentPurgeLock)
            {
                CloseStaleWindows();

                if (Settings.MaxOpenTabs > 0)
                {
                    CloseOldestWindows();
                }
            }
        }

        /// <summary>
        /// Closes a window if it is saved, not active, and not pinned
        /// </summary>
        /// <param name="window">The document window</param>
        /// <returns>True if window was closed</returns>
        private bool CloseDocumentWindow(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DateTime lastWindowAction;

            try
            {
                if (window != VisualStudio.ActiveWindow
                    && (window.Document == null
                        || (window.Document.Saved && !Provider.IsWindowPinned(window.Document.FullName))))
                {
                    documentLastSeen.TryRemove(window, out lastWindowAction);

                    window.Close();
                    return true;
                }
            }
            catch (Exception)
            {
                documentLastSeen.TryRemove(window, out lastWindowAction);
            }

            return false;
        }

        /// <summary>
        ///     Closes stale windows that haven't been viewed recently
        /// </summary>
        private void CloseStaleWindows()
        {
            var allWindows = VisualStudio.Windows.GetDocumentWindows().ToDictionary(x => x);
            var closeMaxTabs = allWindows.Count - Settings.TabCloseThreshold;
            var inactiveWindows = documentLastSeen.GetInactiveTabKeys().ToList();
            var closedTabsCtr = 0;

            foreach (var tab in inactiveWindows.Where(x => allWindows.ContainsKey(x.Window)))
            {
                if (closedTabsCtr >= closeMaxTabs)
                {
                    break;
                }

                Window window = allWindows[tab.Window];

                if (CloseDocumentWindow(window))
                {
                    closedTabsCtr++;
                }
            }

            if (closedTabsCtr > 0)
            {
                Log.Message("Closed {0} tabs that were inactive for longer than {1} minutes", closedTabsCtr, Settings.TabTimeoutMinutes);
            }
        }

        /// <summary>
        ///     Close the oldest windows to keep the maximum open document tab count at threshold
        /// </summary>
        private void CloseOldestWindows()
        {
            var allWindows = VisualStudio.Windows.GetDocumentWindows().ToDictionary(x => x);

            int startingWindowCount = allWindows.Count;
            int documentWindows = startingWindowCount;

            foreach (var documentPath in documentLastSeen.OrderBy(x => x.Value).Select(x => x.Key))
            {
                if (documentWindows <= Settings.MaxOpenTabs)
                {
                    break;
                }

                if (CloseDocumentWindow(allWindows[documentPath]))
                {
                    documentWindows--;
                }
            }

            if (documentWindows != startingWindowCount)
            {
                Log.Message("Closed {0} tabs to maintain a max open document count of {1}", startingWindowCount - documentWindows, Settings.MaxOpenTabs);
            }
        }

        /// <summary>Removes the document from the last seen cache when it is being closed</summary>
        /// <param name="document">The document being closed</param>
        private void DocumentEventsOnDocumentClosing(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                lastAction = DateTime.Now;

                if (document == null)
                {
                    return;
                }

                DateTime value;

                foreach (var window in document.Windows.Cast<Window>())
                {
                    documentLastSeen.TryRemove(window, out value);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        /// <summary>Closes stale windows when a document is saved</summary>
        /// <param name="document">The document being saved</param>
        private void DocumentEventsOnDocumentSaved(Document document)
        {
            try
            {
                lastAction = DateTime.Now;
                TidyTabs(true);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        /// <summary>
        ///     Solution opened event handler initializes the document last seen collection for previously open documents
        /// </summary>
        private void SolutionEventsOnOpened()
        {
            InitWithOpenWindows();
        }

        private void InitWithOpenWindows()
        {
            try
            {
                foreach (var window in VisualStudio.Windows.GetDocumentWindows())
                {
                    UpdateWindowTimestamp(window);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        /// <summary>Text editor line changed event handler</summary>
        /// <param name="startPoint">Starting text point</param>
        /// <param name="endPoint">Ending text point</param>
        /// <param name="hint">Hint value</param>
        private void TextEditorEventsOnLineChanged(TextPoint startPoint, TextPoint endPoint, int hint)
        {
            lastAction = DateTime.Now;
        }

        /// <summary>MenuItem command handler</summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">Event arguments</param>
        private void TidyTabsMenuItemCommandActivated(object sender, EventArgs e)
        {
            try
            {
                Log.Message("Tidy Tabs keyboard shortcut triggered");
                TidyTabs(false);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        /// <summary>Updates the timestamp for a document path</summary>
        /// <param name="window">The document to update</param>
        /// <param name="timestamp">The last activity timestamp</param>
        private void UpdateWindowTimestamp(Window window, DateTime? timestamp = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Ignore tool windows
            try
            {
                if (window == null || window.Linkable)
                {
                    return;
                }
            }
            catch (ObjectDisposedException) 
            {
                return;
            }

            if (!documentLastSeen.ContainsKey(window))
            {
                documentLastSeen.TryAdd(window, timestamp ?? DateTime.Now);
            }
            else
            {
                documentLastSeen[window] = timestamp ?? DateTime.Now;
            }
        }

        /// <summary>Updates the timestamp on the window that is being opened as well as the one losing focus</summary>
        /// <param name="gotFocus">The window gaining focus</param>
        /// <param name="lostFocus">The window losing focus</param>
        private void WindowEventsWindowActivated(Window gotFocus, Window lostFocus)
        {
            try
            {
                lastAction = DateTime.Now;
                UpdateWindowTimestamp(gotFocus);

                if (lostFocus != null)
                {
                    UpdateWindowTimestamp(lostFocus);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        #endregion
    }

    internal static class Extensions
    {
        /// <summary>Enumerates the DTE2 windows Property</summary>
        /// <param name="windows">The DTE2 windows Property</param>
        /// <returns>An enumerable sequence of windows</returns>
        public static IEnumerable<Window> GetDocumentWindows(this Windows windows)
        {
            return windows.Cast<Window>().Where(x => x.Linkable == false);
        }

        /// <summary>Enumerates the document tabs timeout dictionary and returns the key for tabs that are inactive</summary>
        /// <param name="documentTabKeys">Dictionary of document paths and last seen time stamps</param>
        /// <returns>Enumerable sequence of inactive tab keys</returns>
        public static IEnumerable<WindowTimestamp> GetInactiveTabKeys(this ConcurrentDictionary<Window, DateTime> documentTabKeys)
        {
            return
                documentTabKeys.Where(x => (DateTime.Now - x.Value) > TimeSpan.FromMinutes(Settings.Default.TabTimeoutMinutes))
                    .Select(x => new WindowTimestamp(x.Key, x.Value))
                    .OrderByDescending(x => DateTime.Now - x.Timestamp);
        }

        /// <summary>Returns a IVsWindowFrame based on a document path</summary>
        /// <param name="provider">The IServiceProvider used to resolve the IVsWindowFrame</param>
        /// <param name="documentPath">The path to the document in the window</param>
        /// <returns>A IVsWindowFrame reference</returns>
        public static bool IsWindowPinned(this IServiceProvider provider, string documentPath)
        {
            IVsWindowFrame frame;
            IVsUIHierarchy uiHierarchy;
            uint item;

            VsShellUtilities.IsDocumentOpen(provider, documentPath, VSConstants.LOGVIEWID_Primary, out uiHierarchy, out item, out frame);

            return frame != null && frame.IsWindowPinned();
        }

        /// <summary>Checks to see if a IVsWindowFrame is pinned in the document well</summary>
        /// <param name="frame">The IVsWindowFrame reference</param>
        /// <returns>True if the window frame is pinned in the document well</returns>
        private static bool IsWindowPinned(this IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            object result;
            frame.GetProperty((int)__VSFPROPID5.VSFPROPID_IsPinned, out result);
            return Convert.ToBoolean(result);
        }
    }

}
