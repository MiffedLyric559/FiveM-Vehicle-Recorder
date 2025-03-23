using CitizenFX.Core;
using CitizenFX.Core.Native;
using FxEvents;
using System;

namespace RecM.Server
{
    public class Main : BaseScript
    {
        #region Fields

        public static Main Instance;
        public PlayerList Clients;
        public ExportDictionary ExportList;
        private readonly string _resourceName = API.GetCurrentResourceName();
        public bool DebugMode;

        #endregion

        #region Constructor

        public Main()
        {
            EventHub.Initialize();
            Instance = this;
            Clients = Players;
            ExportList = Exports;
            string debugMode = API.GetResourceMetadata(API.GetCurrentResourceName(), "recm_debug_mode", 0);
            DebugMode = debugMode == "yes" || debugMode == "true" || int.TryParse(debugMode, out int num) && num > 0;

            if (_resourceName == "RecM")
            {
                // Load classes
                new Recording();
            }
            else
                "The resource name is invalid, please name it to RecM!".Error();
        }

        #endregion

        #region Tools

        #region Add event handler statically

        public void AddEventHandler(string eventName, Delegate @delegate, bool oldMethod = false)
        {
            if (!oldMethod)
                EventHub.Mount(eventName, FxEvents.Shared.Binding.Remote, @delegate);
            else
                EventHandlers.Add(eventName, @delegate);
        }

        #endregion

        #endregion
    }
}