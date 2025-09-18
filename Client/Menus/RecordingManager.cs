using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FxEvents;
using FxEvents.Shared.TypeExtensions;
using RecM.Client;
using RecM.Client.Utils;
using ScaleformUI;
using ScaleformUI.Menu;
using ScaleformUI.Scaleforms;

namespace RecM.Client.Menus
{
    public class RecordingManager
    {
        #region Fields

        private static UIMenu menu;
        private static UIMenuItem _stopRecordingMenuItem;
        private static UIMenuItem _startRecordingMenuItem;
        private static UIMenuItem _discardRecordingMenuItem;
        private static UIMenuItem _saveRecordingMenuItem;
        private static UIMenuItem _createRecordingsMenuItem;
        private static List<string> _lastVanillaRecordings = null;
        private static Dictionary<string, RecordingListing> _lastCustomRecordings = null;
        private static int _lastVanillaRecordingsMenuIndex;
        private static int _lastCustomRecordingsMenuIndex;
        public static InstructionalButton SwitchPlaybackSpeedDisplayBtn;

        #endregion

        #region Constructor

        public RecordingManager()
        {
            Main.Instance.RegisterKeyMapping("recm_menu", "Vehicle Recording Utility.", "F7", new Action<int, List<object>, string>(async (source, args, rawCommand) =>
            {
                bool success = await EventDispatcher.Get<bool>("RecM:openMenu:Server");
                if (success && !MenuHandler.IsAnyMenuOpen)
                    menu.Visible = true;
            }));

            CreateMenu();
        }

        #endregion

        #region Tools

        private static void RefreshActiveSessionsMenu(UIMenu menu)
        {
            menu.Clear();

            var sessions = PlaybackSessionManager.Sessions;
            if (sessions.Count == 0)
            {
                var emptyItem = new UIMenuItem("No active sessions", "There are no playback sessions running.") { Enabled = false };
                menu.AddItem(emptyItem);
                return;
            }

            foreach (var session in sessions)
            {
                var status = PlaybackSessionManager.GetStatusText(session);
                var descriptionBuilder = new StringBuilder();
                descriptionBuilder.AppendLine($"Status: {status}");
                if (session.Vehicle != null && session.Vehicle.Exists())
                    descriptionBuilder.AppendLine($"Vehicle: {session.Vehicle.DisplayName}");
                else if (!string.IsNullOrEmpty(session.VehicleModel))
                    descriptionBuilder.AppendLine($"Vehicle: {session.VehicleModel}");

                if (session.StartPosition != null)
                {
                    var start = session.StartPosition.Value;
                    descriptionBuilder.AppendLine($"Origin: {start.X:F2}, {start.Y:F2}, {start.Z:F2}");
                }

                var displayName = string.IsNullOrWhiteSpace(session.DisplayName) ? session.RecordingName : session.DisplayName;
                var item = new UIMenuItem(displayName, descriptionBuilder.ToString().Trim());
                item.SetRightLabel(session.IsPlayerControlled ? "Player" : "Autonomous");
                var capturedSession = session;
                item.Activated += async (sender, e) =>
                {
                    if (capturedSession.IsPlayerControlled)
                        await Recording.StopRecordingPlayback();
                    else
                        await PlaybackSessionManager.StopSession(capturedSession.Id, PlaybackStopReason.Cancelled);

                    RefreshActiveSessionsMenu(menu);
                };

                menu.AddItem(item);
            }
        }

        private static bool ShouldRefreshCustomRecordings(Dictionary<string, RecordingListing> previous, Dictionary<string, RecordingListing> current)
        {
            if (current == null)
                return false;

            if (previous == null || previous.Count != current.Count)
                return true;

            foreach (var kvp in current)
            {
                if (!previous.TryGetValue(kvp.Key, out var previousListing))
                    return true;

                if (!previousListing.StartPosition.Equals(kvp.Value.StartPosition))
                    return true;

                var previousMetadata = Json.Stringify(previousListing.Metadata) ?? string.Empty;
                var currentMetadata = Json.Stringify(kvp.Value.Metadata) ?? string.Empty;
                if (!previousMetadata.Equals(currentMetadata, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void OpenPlaybackModeMenu(UIMenu parentMenu, string displayName, int recordingId, string recordingName, string model = null, Vector4? pos = null, RecordingMetadata metadata = null)
        {
            var optionsMenu = new UIMenu(displayName, "Playback Options");
            optionsMenu.ControlDisablingEnabled = false;

            var takeControlItem = new UIMenuItem("Take Control", "Spawn or reuse your vehicle and play the recording from the driver's seat.");
            takeControlItem.Activated += async (sender, e) =>
            {
                await Recording.PlayRecording(recordingId, recordingName, model, pos, true, false, displayName, metadata);
            };
            optionsMenu.AddItem(takeControlItem);

            var autonomousItem = new UIMenuItem("Spawn Autonomous", "Spawn a networked vehicle with a dummy driver to replay this recording independently.");
            autonomousItem.Activated += async (sender, e) =>
            {
                var session = await Recording.PlayRecording(recordingId, recordingName, model, pos, false, true, displayName, metadata);
                if (session != null)
                    $"Started autonomous playback for {displayName}.".Log(true);
            };
            optionsMenu.AddItem(autonomousItem);

            parentMenu.SwitchTo(optionsMenu, inheritOldMenuParams: true);
        }

        #region Create menu

        public async static void CreateMenu()
        {
            if (MenuHandler.IsAnyMenuOpen) return;

            menu = new UIMenu("RecM", "Vehicle Recording Utility", new PointF(960, 20), "recm_textures", "recm_banner", true);
            menu.ControlDisablingEnabled = false;
            menu.MaxItemsOnScreen = 15;

            #region Create recordings

            _createRecordingsMenuItem = new UIMenuItem("Create Recording", "Create your own recordings which will save to your Saved Recordings menu.");
            _createRecordingsMenuItem.SetRightLabel("→→→");
            menu.AddItem(_createRecordingsMenuItem);
            UIMenu createRecordingsMenu = new UIMenu("Create Recording", "Create Recording");
            createRecordingsMenu.ControlDisablingEnabled = false;
            _createRecordingsMenuItem.Activated += (sender, e) =>
            {
                sender.SwitchTo(createRecordingsMenu, inheritOldMenuParams: true);
            };

            _startRecordingMenuItem = new UIMenuItem("Start Recording", "Start recording the vehicle's data.");
            createRecordingsMenu.AddItem(_startRecordingMenuItem);
            _startRecordingMenuItem.Activated += async (sender, e) =>
            {
                // This is gonna be behind a locked item anyways
                if (Recording.IsRecording)
                {
                    "There's a recording being made at the moment, please wait...".Alert(true);
                    return;
                }
                if (!Game.PlayerPed.IsInVehicle())
                {
                    "You need to be in a vehicle to start recording.".Alert(true);
                    return;
                }
                if (Game.PlayerPed.CurrentVehicle == null)
                {
                    "Your vehicle is null, you can't record with this.".Alert(true);
                    return;
                }
                if (!Game.PlayerPed.CurrentVehicle.Exists())
                {
                    "Your vehicle doesn't exist, you can't record like this.".Alert(true);
                    return;
                }
                if (Game.PlayerPed.CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver) != Game.PlayerPed)
                {
                    "You aren't the driver, you need to be to record with this vehicle.".Alert(true);
                    return;
                }

                _startRecordingMenuItem.Enabled = false;
                _startRecordingMenuItem.Description = "Recording...";
                _stopRecordingMenuItem.Enabled = true;
                Recording.StartRecording();
            };

            _stopRecordingMenuItem = new UIMenuItem("Stop Recording", "Stop recording the vehicle's data.") { Enabled = false };
            createRecordingsMenu.AddItem(_stopRecordingMenuItem);
            _stopRecordingMenuItem.Activated += async (sender, e) =>
            {
                // This is gonna be behind a locked item anyways
                if (!Recording.IsRecording)
                {
                    "There's nothing being recorded at the moment".Alert(true);
                    return;
                }

                _startRecordingMenuItem.Description = "Save or discard your recording.";
                _stopRecordingMenuItem.Enabled = false;
                _stopRecordingMenuItem.Description = "Save or discard your recording.";
                _discardRecordingMenuItem.Enabled = true;
                _saveRecordingMenuItem.Enabled = true;
                Recording.StopRecording();
            };

            _discardRecordingMenuItem = new UIMenuItem("~r~Discard Recording", "Discard the recording you've just recorded.") { Enabled = false };
            createRecordingsMenu.AddItem(_discardRecordingMenuItem);
            _discardRecordingMenuItem.Activated += async (sender, e) =>
            {
                _startRecordingMenuItem.Enabled = true;
                _startRecordingMenuItem.Description = "Start recording the vehicle's data.";
                _stopRecordingMenuItem.Description = "Stop recording the vehicle's data.";
                _discardRecordingMenuItem.Enabled = false;
                _saveRecordingMenuItem.Enabled = false;
                Recording.DiscardRecording();
            };

            _saveRecordingMenuItem = new UIMenuItem("~g~Save Recording", "Save the recording to your Saved Recordings menu.") { Enabled = false };
            createRecordingsMenu.AddItem(_saveRecordingMenuItem);
            _saveRecordingMenuItem.Activated += async (sender, e) =>
            {
                _saveRecordingMenuItem.Enabled = false;
                _discardRecordingMenuItem.Enabled = false;

                var ui = await Tools.GetUserInput("Enter a name for your recording", 30);
                if (!string.IsNullOrEmpty(ui))
                {
                    // Join the words together since we can't have spaces in the name
                    ui = ui.Replace(" ", "");
                    var success = await Recording.SaveRecording(ui);
                    if (success)
                    {
                        _startRecordingMenuItem.Enabled = true;
                        _startRecordingMenuItem.Description = "Start recording the vehicle's data.";
                        _stopRecordingMenuItem.Description = "Stop recording the vehicle's data.";
                        _discardRecordingMenuItem.Enabled = false;
                        _saveRecordingMenuItem.Enabled = false;
                        sender.RefreshMenu();
                    }
                    else
                    {
                        _saveRecordingMenuItem.Enabled = true;
                        _discardRecordingMenuItem.Enabled = true;
                    }
                }
            };

            #endregion

            #region Saved recordings

            UIMenuItem savedRecordingsMenuItem = new UIMenuItem("Saved Recordings", "This menu contains all the saved recordings.");
            savedRecordingsMenuItem.SetRightLabel("→→→");
            menu.AddItem(savedRecordingsMenuItem);
            UIMenu savedRecordingsMenu = new UIMenu("Saved Recordings", "All Saved Recordings");
            savedRecordingsMenu.ControlDisablingEnabled = false;
            savedRecordingsMenuItem.Activated += (sender, e) =>
            {
                sender.SwitchTo(savedRecordingsMenu, inheritOldMenuParams: true);
            };

            UIMenuItem vanillaRecordingsMenuItem = new UIMenuItem("Vanilla", "This menu contains all the vanilla recording data.");
            vanillaRecordingsMenuItem.SetRightLabel("→→→");
            savedRecordingsMenu.AddItem(vanillaRecordingsMenuItem);
            UIMenu vanillaRecordingsMenu = new UIMenu("Vanilla", "All Vanilla Recordings");
            vanillaRecordingsMenu.ControlDisablingEnabled = false;
            vanillaRecordingsMenuItem.Activated += (sender, e) =>
            {
                sender.SwitchTo(vanillaRecordingsMenu, inheritOldMenuParams: true, newMenuCurrentSelection: _lastVanillaRecordingsMenuIndex);
            };

            UIMenuItem customRecordingsMenuItem = new UIMenuItem("Custom", "This menu contains all the custom recording data.");
            customRecordingsMenuItem.SetRightLabel("→→→");
            savedRecordingsMenu.AddItem(customRecordingsMenuItem);
            UIMenu customRecordingsMenu = new UIMenu("Custom", "All Custom Recordings");
            customRecordingsMenu.ControlDisablingEnabled = false;
            customRecordingsMenuItem.Activated += (sender, e) =>
            {
                sender.SwitchTo(customRecordingsMenu, inheritOldMenuParams: true, newMenuCurrentSelection: _lastCustomRecordingsMenuIndex);
            };

            UIMenuItem sessionsMenuItem = new UIMenuItem("Active Sessions", "View and manage current playback sessions.");
            sessionsMenuItem.SetRightLabel("→→→");
            savedRecordingsMenu.AddItem(sessionsMenuItem);
            UIMenu sessionsMenu = new UIMenu("Active Sessions", "Playback Sessions");
            sessionsMenu.ControlDisablingEnabled = false;
            sessionsMenu.OnMenuOpen += (menu, data) =>
            {
                RefreshActiveSessionsMenu(sessionsMenu);
            };
            sessionsMenuItem.Activated += (sender, e) =>
            {
                RefreshActiveSessionsMenu(sessionsMenu);
                sender.SwitchTo(sessionsMenu, inheritOldMenuParams: true);
            };

            menu.OnMenuOpen += async (menu, data) =>
            {
                savedRecordingsMenuItem.Enabled = false;
                savedRecordingsMenuItem.Description = "Loading...";

                // Get the recordings
                var recordings = await Recording.GetRecordings();
                var vanilla = recordings.Item1;
                var custom = recordings.Item2 ?? new Dictionary<string, RecordingListing>();

                savedRecordingsMenuItem.Enabled = true;
                savedRecordingsMenuItem.Description = "This menu contains all the saved recordings.";

                #region Vanilla recordings

                if (_lastVanillaRecordings == null || !_lastVanillaRecordings.SequenceEqual(vanilla))
                {
                    _lastVanillaRecordingsMenuIndex = 0;
                    _lastVanillaRecordings = vanilla;
                    vanillaRecordingsMenu.Clear();
                    vanillaRecordingsMenu.InstructionalButtons.RemoveAll(x => !x.Text.Equals("Back") && !x.Text.Equals("Select"));
                    if (vanilla.Count > 0)
                    {
                        vanillaRecordingsMenuItem.Enabled = true;
                        vanillaRecordingsMenuItem.Description = "This menu contains all the vanilla recording data.";
                        vanillaRecordingsMenuItem.SetRightBadge(BadgeIcon.NONE);

                        Dictionary<string, List<string>> vanillaRecordings = [];
                        foreach (var recording in vanilla)
                        {
                            string name = recording.Substring(0, recording.Length - 3);
                            string id = recording.Substring(recording.Length - 3);

                            if (!vanillaRecordings.ContainsKey(name))
                                vanillaRecordings.Add(name, [id]);
                            else
                                vanillaRecordings[name].Add(id);
                        }

                        var filterBtn = new InstructionalButton(Control.LookBehind, Control.LookBehind, "Filter");
                        vanillaRecordingsMenu.InstructionalButtons.Add(filterBtn);
                        filterBtn.OnControlSelected += (button) =>
                        {
                            _ = Task.Run(async () =>
                            {
                                //"This feature is currently disabled due to a flaw in the menu API.".Alert(true);
                                string filter = await Tools.GetUserInput("Enter a word (leave blank to reset)", 20);

                                if (string.IsNullOrEmpty(filter))
                                {
                                    // Check if the menu is filtered
                                    if (vanillaRecordingsMenu._unfilteredMenuItems.Count > 0)
                                    {
                                        "The filter has been reset.".Alert();
                                        vanillaRecordingsMenu.ResetFilter();
                                    }

                                    return;
                                }

                                // Check if the menu is filtered
                                if (vanillaRecordingsMenu._unfilteredMenuItems.Count > 0)
                                {
                                    "The filter has been reset.".Alert();
                                    vanillaRecordingsMenu.ResetFilter();
                                }

                                // Filter the menu items
                                vanillaRecordingsMenu.FilterMenuItems((mb) => mb.Label.ToLower().Contains(filter.ToLower()));
                            });

                            return button;
                        };

                        var stopRecordingBtn = new InstructionalButton(Control.Jump, Control.Jump, "Stop Playback");
                        vanillaRecordingsMenu.InstructionalButtons.Add(stopRecordingBtn);
                        stopRecordingBtn.OnControlSelected += (button) =>
                        {
                            _ = Recording.StopRecordingPlayback();
                            return button;
                        };

                        var switchPlaybackSpeedNextBtn = new InstructionalButton(Control.FrontendRb, Control.FrontendLs, $"Faster");
                        vanillaRecordingsMenu.InstructionalButtons.Add(switchPlaybackSpeedNextBtn);
                        switchPlaybackSpeedNextBtn.OnControlSelected += (button) =>
                        {
                            Recording.SwitchPlaybackSpeed(Recording.GetPlaybackSpeedIndex() + 1);
                            return button;
                        };

                        var switchPlaybackSpeedPrevBtn = new InstructionalButton(Control.FrontendLb, Control.FrontendRs, $"Slower");
                        vanillaRecordingsMenu.InstructionalButtons.Add(switchPlaybackSpeedPrevBtn);
                        switchPlaybackSpeedPrevBtn.OnControlSelected += (button) =>
                        {
                            Recording.SwitchPlaybackSpeed(Recording.GetPlaybackSpeedIndex() - 1);
                            return button;
                        };

                        SwitchPlaybackSpeedDisplayBtn = new InstructionalButton([], $"Speed {Recording.GetPlaybackSpeedName()}");
                        vanillaRecordingsMenu.InstructionalButtons.Add(SwitchPlaybackSpeedDisplayBtn);

                        foreach (var recording in vanillaRecordings)
                        {
                            if (!vanillaRecordingsMenu.MenuItems.Any(x => x.Label.Equals(recording.Key)))
                            {
                                var listItem = new UIMenuListItem(recording.Key, [], 0);
                                vanillaRecordingsMenu.AddItem(listItem);
                                listItem.ItemData = recording.Value;
                                foreach (var id in recording.Value)
                                    listItem.Items.Add(id);

                                // Reorder the items by ID from lowest to highest
                                listItem.Items = listItem.Items.OrderBy(x => x).ToList();

                                listItem.OnListSelected += (item, index) =>
                                {
                                    if (Recording.IsLoadingRecording)
                                    {
                                        "There's a recording being loaded at the moment, please wait...".Alert(true);
                                        return;
                                    }

                                    var selectedId = int.Parse(item.Items[index].ToString());
                                    OpenPlaybackModeMenu(vanillaRecordingsMenu, item.Label, selectedId, item.Label);
                                };
                            }
                            else
                            {
                                var listItem = vanillaRecordingsMenu.MenuItems.FirstOrDefault(x => x.Label.Equals(recording.Key)) as UIMenuListItem;
                                foreach (var id in recording.Value)
                                    listItem.Items.Add(id);

                                // Reorder the items by ID from lowest to highest
                                listItem.Items = listItem.Items.OrderBy(x => x).ToList();
                            }
                        }

                        vanillaRecordingsMenu.MenuItems.Sort((a, b) => { return a.Label.ToLower().CompareTo(b.Label.ToLower()); });
                    }
                    else
                    {
                        vanillaRecordingsMenuItem.Enabled = false;
                        vanillaRecordingsMenuItem.Description = "This menu contains no vanilla recordings.";
                        vanillaRecordingsMenuItem.SetRightBadge(BadgeIcon.LOCK);
                    }
                }

                #endregion

                #region Custom recordings

                if (ShouldRefreshCustomRecordings(_lastCustomRecordings, custom))
                {
                    _lastCustomRecordingsMenuIndex = 0;
                    _lastCustomRecordings = custom;
                    customRecordingsMenu.Clear();
                    if (custom.Count > 0)
                    {
                        customRecordingsMenuItem.Enabled = true;
                        customRecordingsMenuItem.Description = "This menu contains all the custom recording data.";
                        customRecordingsMenuItem.SetRightBadge(BadgeIcon.NONE);

                        foreach (var recording in custom)
                        {
                            var name = recording.Key.Split('_')[0];
                            var model = recording.Key.Split('_')[1];
                            var id = int.Parse(recording.Key.Split('_')[2]);
                            var listing = recording.Value;
                            Vector4? pos = listing != null ? listing.StartPosition : null;
                            var metadata = listing?.Metadata;

                            UIMenuItem recordItem = new UIMenuItem(name, $"Vehicle: {model}\nID: {id}");
                            recordItem.ItemData = recording;
                            recordItem.SetRightLabel("→→→");
                            customRecordingsMenu.AddItem(recordItem);
                            UIMenu recordItemMenu = new UIMenu(name, name);
                            recordItemMenu.ControlDisablingEnabled = false;
                            recordItem.Activated += (sender, e) =>
                            {
                                sender.SwitchTo(recordItemMenu, inheritOldMenuParams: true);

                                // Update the playback speed display (best place to do it)
                                ((UIMenuDynamicListItem)recordItemMenu.MenuItems.FirstOrDefault(x => x.Label.Equals("Playback Speed"))).CurrentListItem = Recording.GetPlaybackSpeedName();
                            };

                            UIMenuItem takeControlItem = new UIMenuItem("Take Control", "Play the recording from your perspective.");
                            recordItemMenu.AddItem(takeControlItem);
                            takeControlItem.Activated += async (sender, e) =>
                            {
                                await Recording.PlayRecording(id, $"{name}_{model}_", model, pos, true, false, name, metadata);
                            };

                            UIMenuItem autonomousItem = new UIMenuItem("Spawn Autonomous", "Spawn a networked vehicle to play this recording autonomously.");
                            recordItemMenu.AddItem(autonomousItem);
                            autonomousItem.Activated += async (sender, e) =>
                            {
                                var session = await Recording.PlayRecording(id, $"{name}_{model}_", model, pos, false, true, name, metadata);
                                if (session != null)
                                    $"Started autonomous playback for {name}.".Log(true);
                            };

                            var playbackSpeedItem = new UIMenuDynamicListItem("Playback Speed", "Change the playback speed.", Recording.GetPlaybackSpeedName(), async (item, dir) =>
                            {
                                if (dir == ChangeDirection.Left)
                                {
                                    if (Recording.GetPlaybackSpeedIndex() == 0)
                                        return Recording.GetPlaybackSpeedName();

                                    Recording.SwitchPlaybackSpeed(Recording.GetPlaybackSpeedIndex() - 1);
                                }
                                else if (dir == ChangeDirection.Right)
                                {
                                    if (Recording.GetPlaybackSpeedIndex() == Recording.GetPlaybackSpeedNameList().Count - 1)
                                        return Recording.GetPlaybackSpeedName();

                                    Recording.SwitchPlaybackSpeed(Recording.GetPlaybackSpeedIndex() + 1);
                                }

                                return Recording.GetPlaybackSpeedName();
                            });
                            recordItemMenu.AddItem(playbackSpeedItem);

                            UIMenuItem stopItem = new UIMenuItem("Stop", "Stop the recording.");
                            recordItemMenu.AddItem(stopItem);
                            stopItem.Activated += async (sender, e) =>
                            {
                                await Recording.StopRecordingPlayback();
                            };

                            UIMenuItem deleteItem = new UIMenuItem("~r~Delete", "Delete the recording.");
                            recordItemMenu.AddItem(deleteItem);
                            deleteItem.Activated += async (sender, e) =>
                            {
                                var success = await Recording.DeleteRecording(name, model);
                                if (success)
                                {
                                    sender.GoBack();
                                    customRecordingsMenu.GoBack();
                                    savedRecordingsMenu.GoBack();
                                }
                            };
                        }
                    }
                    else
                    {
                        customRecordingsMenuItem.Enabled = false;
                        customRecordingsMenuItem.Description = "This menu contains no custom recordings.";
                        customRecordingsMenuItem.SetRightBadge(BadgeIcon.LOCK);
                    }
                }

                #endregion
            };

            vanillaRecordingsMenu.OnIndexChange += (menu, index) =>
            {
                _lastVanillaRecordingsMenuIndex = index;
            };

            customRecordingsMenu.OnIndexChange += (menu, index) =>
            {
                _lastCustomRecordingsMenuIndex = index;
            };

            #endregion

            #region Credits

            var creditsMenuItem = new UIMenuItem("Credits", "All of the people that helped with the creation of the script either directly or indirectly.");
            creditsMenuItem.SetRightBadge(BadgeIcon.ROCKSTAR);
            menu.AddItem(creditsMenuItem);
            UIMenu creditsMenu = new UIMenu("Credits", "Credits");
            creditsMenu.ControlDisablingEnabled = false;
            creditsMenuItem.Activated += (sender, e) =>
            {
                sender.SwitchTo(creditsMenu, inheritOldMenuParams: true);
            };

            var dexyfexItem = new UIMenuItem("Dexyfex", "Author of Codewalker, it provided the tools for ovr -> yvr conversion.");
            creditsMenu.AddItem(dexyfexItem);
            dexyfexItem.SetRightLabel("(Click To Visit Repo)");
            dexyfexItem.Activated += async (sender, e) =>
            {
                "The link will now open in your browser.".Warning(true);
                await BaseScript.Delay(3000);
                API.SendNuiMessage(Json.Stringify(new { url = "https://github.com/dexyfex/CodeWalker" }));
            };
            var manups4eItem = new UIMenuItem("Manups4e", "Author of ScaleformUI, this menu's API.");
            creditsMenu.AddItem(manups4eItem);
            manups4eItem.SetRightLabel("(Click To Visit Repo)");
            manups4eItem.Activated += async (sender, e) =>
            {
                "The link will now open in your browser.".Warning(true);
                await BaseScript.Delay(3000);
                API.SendNuiMessage(Json.Stringify(new { url = "https://github.com/manups4e/ScaleformUI" }));
            };
            var lucas7yoshiItem = new UIMenuItem("Lucas7yoshi", "For providing great help and research for the vehicle recordings from within the Codewalker Discord.");
            creditsMenu.AddItem(lucas7yoshiItem);

            #endregion
        }

        #endregion

        #endregion
    }
}
