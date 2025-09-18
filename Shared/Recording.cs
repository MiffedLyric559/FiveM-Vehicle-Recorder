using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Drawing;
using FxEvents.Shared.TypeExtensions;

#if CLIENT

using RecM.Client;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using CitizenFX.Core.UI;
using FxEvents;
using RecM.Client.Utils;
using RecM.Client.Menus;

#endif

#if SERVER

using System.IO;
using System.Xml;
using RecM.Server;
using CodeWalker.GameFiles;
using CitizenFX.Core.Native;
using CitizenFX.Core;
using FxEvents;

#endif

namespace RecM
{
    public class Recording
    {
        #region Fields

#if CLIENT

        /// <summary>
        /// To indicate if the recording is loaded or not.
        /// </summary>
        public static bool IsLoadingRecording;

        /// <summary>
        /// Whether the player's recording a playback.
        /// </summary>
        public static bool IsRecording;

        /// <summary>
        /// The default vehicle to use if the current vehicle or model doesn't exist.
        /// </summary>
        private static readonly string _defaultVehicle = "dubsta2";

        /// <summary>
        /// The current recording data.
        /// </summary>
        private static readonly List<Record> _currRecording = [];

        /// <summary>
        /// The start time of the recording.
        /// </summary>
        private static int _recordingStartTime;

        /// <summary>
        /// The last vehicle of the player.
        /// </summary>
        private static string _lastVehicleModel;

        /// <summary>
        /// The last location of the player.
        /// </summary>
        private static Vector4? _lastLocation = null;

        /// <summary>
        /// Just adds a cooldown after the recording is played.
        /// </summary>
        private static bool _recordingCooldown = false;

        private static bool _isPlaybackCheckerAttached;

        /// <summary>
        /// This has to store the client's original cinematic cam state.
        /// </summary>
        private static bool _cinematicCamBlocked;

        /// <summary>
        /// The playback speeds.
        /// </summary>
        private static List<float> _playbackSpeeds = [ -16, -8, -4, -2, -1.75f, -1.5f, -1.25f, -1, -0.75f, -0.5f, -0.25f, 0, 0.25f, 0.50f, 0.75f, 1, 1.25f, 1.50f, 1.75f, 2, 4, 8, 16 ];

        /// <summary>
        /// The current playback speed index.
        /// </summary>
        private static int _currPlaybackSpeedIndex = _playbackSpeeds.IndexOf(1);

#endif

        #endregion

        #region Constructor

#if CLIENT

        public Recording()
        {
            Main.Instance.AddEventHandler("RecM:registerRecording:Client", new Action<string, string>(RegisterRecording));
            Main.Instance.AttachTick(GeneralThread);

            // Only at startup
            LoadTextures();
        }

#endif

#if SERVER

        public Recording()
        {
            Main.Instance.AddEventHandler("RecM:saveRecording:Server", new Action<string, string, string, string, bool, NetworkCallbackDelegate>(SaveRecording), true);
            Main.Instance.AddEventHandler("RecM:deleteRecording:Server", new Func<Player, string, string, Task<Tuple<bool, string>>>(DeleteRecording));
            Main.Instance.AddEventHandler("RecM:getRecordings:Server", new Func<Player, Task<Dictionary<string, RecordingListing>>>(GetRecordings));
            Main.Instance.AddEventHandler("RecM:openMenu:Server", new Func<Player, Task<bool>>(OpenMenu));

            // Only at startup
            CleanRecordings();
        }

#endif

        #endregion

        #region Events

#if CLIENT

        #region Register recording

        private void RegisterRecording(string name, string cacheString) => API.RegisterStreamingFileFromCache("RecM_records", name, cacheString);

        #endregion

#endif

#if SERVER

        #region Save recording

        private void SaveRecording(string name, string model, string data, string metadataJson, bool overwrite, NetworkCallbackDelegate cb)
        {
            try
            {
                // If the resource doesn't exist
                if (!Directory.Exists(API.GetResourcePath("RecM_records")))
                {
                    "You need to have the RecM_records resource installed.".Error();
                    cb(false, "You need to have the RecM_records resource installed.");
                    return;
                }

                // Parse the data
                var recordings = Json.Parse<List<Record>>(data);

                // This will be used to load/save meta files
                XmlDocument doc = new();

                // Load the xml
                doc.LoadXml("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "\n<VehicleRecordList>" +
                    "\n</VehicleRecordList>");

                // Loop through the recordings and add the children to the xml
                foreach (Record recording in recordings)
                {
                    // Declare the item element
                    XmlElement itemElement = doc.CreateElement("Item");

                    // Time
                    XmlElement timeElement = doc.CreateElement("Time");
                    timeElement.SetAttribute("value", recording.Time.ToString());
                    itemElement.AppendChild(timeElement);

                    // Position
                    XmlElement posElement = doc.CreateElement("Position");
                    posElement.SetAttribute("x", recording.Position.X.ToString("G"));
                    posElement.SetAttribute("y", recording.Position.Y.ToString("G"));
                    posElement.SetAttribute("z", recording.Position.Z.ToString("G"));
                    itemElement.AppendChild(posElement);

                    // Velocity
                    XmlElement velElement = doc.CreateElement("Velocity");
                    velElement.SetAttribute("x", recording.Velocity.X.ToString("G"));
                    velElement.SetAttribute("y", recording.Velocity.Y.ToString("G"));
                    velElement.SetAttribute("z", recording.Velocity.Z.ToString("G"));
                    itemElement.AppendChild(velElement);

                    // Top/Forward
                    XmlElement topElement = doc.CreateElement("Forward");
                    topElement.SetAttribute("x", recording.Forward.X.ToString("G"));
                    topElement.SetAttribute("y", recording.Forward.Y.ToString("G"));
                    topElement.SetAttribute("z", recording.Forward.Z.ToString("G"));
                    itemElement.AppendChild(topElement);

                    // Right
                    XmlElement rightElement = doc.CreateElement("Right");
                    rightElement.SetAttribute("x", recording.Right.X.ToString("G"));
                    rightElement.SetAttribute("y", recording.Right.Y.ToString("G"));
                    rightElement.SetAttribute("z", recording.Right.Z.ToString("G"));
                    itemElement.AppendChild(rightElement);

                    // Steering
                    XmlElement steerElement = doc.CreateElement("Steering");
                    steerElement.SetAttribute("value", recording.SteeringAngle.ToString("G"));
                    itemElement.AppendChild(steerElement);

                    // Gas
                    XmlElement gasElement = doc.CreateElement("GasPedal");
                    gasElement.SetAttribute("value", recording.Gas.ToString("G"));
                    itemElement.AppendChild(gasElement);

                    // Brake
                    XmlElement brakeElement = doc.CreateElement("BrakePedal");
                    brakeElement.SetAttribute("value", recording.Brake.ToString("G"));
                    itemElement.AppendChild(brakeElement);

                    // Handbrake
                    XmlElement handbrakeElement = doc.CreateElement("Handbrake");
                    handbrakeElement.SetAttribute("value", recording.UseHandbrake.ToString());
                    itemElement.AppendChild(handbrakeElement);

                    // Finally add the item element
                    doc["VehicleRecordList"].AppendChild(itemElement);
                }

                // Now, convert to proper yvr format
                var yvr = XmlYvr.GetYvr(doc);
                var yvrData = yvr.Save();

                // Move it to the recordings resource
                var recordingsPath = Path.Combine(API.GetResourcePath("RecM_records"), "stream");
                if (!Directory.Exists(recordingsPath))
                    Directory.CreateDirectory(recordingsPath);

                string targetBaseName;
                if (!File.Exists(Path.Combine(recordingsPath, $"{name}_{model}_001.yvr")))
                {
                    targetBaseName = $"{name}_{model}_001";
                    File.WriteAllBytes(Path.Combine(recordingsPath, $"{targetBaseName}.yvr"), yvrData);
                    var cacheString = API.RegisterResourceAsset("RecM_records", $"stream/{targetBaseName}.yvr");
                    EventDispatcher.Send(Main.Instance.Clients, "RecM:registerRecording:Client", $"{targetBaseName}.yvr", cacheString);
                }
                else
                {
                    // Optional depending on the context
                    if (overwrite)
                    {
                        // Now if we're here, the file existed, now we need to find the highest number and add 1 to it
                        var maxValue = Directory.EnumerateFiles(recordingsPath, "*.yvr")
                            .Where(x => Path.GetFileNameWithoutExtension(x).StartsWith($"{name}_{model}"))
                            .Select(x => int.Parse(Path.GetFileNameWithoutExtension(x).Split('_')[2]))
                            .DefaultIfEmpty(0)
                            .Max();

                        targetBaseName = $"{name}_{model}_{(maxValue + 1).ToString().PadLeft(3, '0')}";
                        File.WriteAllBytes(Path.Combine(recordingsPath, $"{targetBaseName}.yvr"), yvrData);
                        var cacheString = API.RegisterResourceAsset("RecM_records", $"stream/{targetBaseName}.yvr");
                        EventDispatcher.Send(Main.Instance.Clients, "RecM:registerRecording:Client", $"{targetBaseName}.yvr", cacheString);
                    }
                    else
                    {
                        cb(false, "A recording with this name and model already exists.");
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(metadataJson))
                {
                    File.WriteAllText(Path.Combine(recordingsPath, $"{targetBaseName}.json"), metadataJson);
                }

                // Callback telling the client that the recording was saved
                cb(true, "Success!");
            }
            catch (Exception ex)
            {
                ex.ToString().Error();
                cb(false, "There was an error with saving the recording, please check the server console for an exception.");
            }
        }

        #endregion

        #region Delete recording

        private async Task<Tuple<bool, string>> DeleteRecording([FromSource] Player source, string name, string model)
        {
            try
            {
                // If the resource doesn't exist
                if (!Directory.Exists(API.GetResourcePath("RecM_records")))
                {
                    "You need to have the RecM_records resource installed.".Error();
                    return new Tuple<bool, string>(false, "You need to have the RecM_records resource installed.");
                }

                // The path to the recordings
                var recordingsPath = Path.Combine(API.GetResourcePath("RecM_records"), "stream");

                // Delete all files that start with the name and model
                foreach (var file in Directory.EnumerateFiles(recordingsPath))
                {
                    // Looking for all of them since there might be multiple recordings with the same name and model due to the overwriting process
                    if (Path.GetFileNameWithoutExtension(file).StartsWith($"{name}_{model}"))
                        File.Delete(file);
                }

                // Finally, callback telling the client that the recording was saved
                return new Tuple<bool, string>(true, "Success!");
            }
            catch (Exception ex)
            {
                ex.ToString().Error();
                return new Tuple<bool, string>(false, "There was an error with deleting the recording, please check the server console for an exception.");
            }
        }

        #endregion

        #region Get recordings

        private async Task<Dictionary<string, RecordingListing>> GetRecordings([FromSource] Player source)
        {
            try
            {
                // If the resource doesn't exist
                if (!Directory.Exists(API.GetResourcePath("RecM_records")))
                {
                    "You need to have the RecM_records resource installed.".Error();
                    return [];
                }

                // The path to the recordings
                var recordingsPath = Path.Combine(API.GetResourcePath("RecM_records"), "stream");
                if (!Directory.Exists(recordingsPath))
                    Directory.CreateDirectory(recordingsPath);

                // For custom recordings, find all files that start with the name and model
                Dictionary<string, RecordingListing> recordings = [];
                foreach (var file in Directory.EnumerateFiles(recordingsPath, "*.yvr"))
                {
                    // Just to be safe
                    if (!Path.GetFileNameWithoutExtension(file).Contains("_"))
                        continue;

                    var name = Path.GetFileNameWithoutExtension(file).Split('_')[0];
                    var model = Path.GetFileNameWithoutExtension(file).Split('_')[1];
                    var id = Path.GetFileNameWithoutExtension(file).Split('_')[2];
                    var maxValue = Directory.EnumerateFiles(recordingsPath, "*.yvr")
                        .Where(x => Path.GetFileNameWithoutExtension(x).StartsWith($"{name}_{model}"))
                        .Select(x => int.Parse(Path.GetFileNameWithoutExtension(x).Split('_')[2]))
                        .Max();

                    // Get the recording's start position
                    var yvr = RpfFile.GetResourceFile<YvrFile>(File.ReadAllBytes(file));
                    var yvrData = yvr.Save();
                    var xml = YvrXml.GetXml(yvr);

                    // This will be used to load/save meta files
                    XmlDocument doc = new();

                    // Load the xml
                    doc.LoadXml(xml);

                    // Grab the position from the xml
                    XmlElement posAttr = doc["VehicleRecordList"].ChildNodes[0]["Position"];
                    var posX = float.Parse(posAttr.GetAttribute("x"));
                    var posY = float.Parse(posAttr.GetAttribute("y"));
                    var posZ = float.Parse(posAttr.GetAttribute("z"));
                    var pos = new Vector3(posX, posY, posZ);

                    // Calculate the heading from the forward vector
                    XmlElement forAttr = doc["VehicleRecordList"].ChildNodes[0]["Forward"];
                    var forX = float.Parse(forAttr.GetAttribute("x"));
                    var forY = float.Parse(forAttr.GetAttribute("y"));
                    var forZ = float.Parse(forAttr.GetAttribute("z"));
                    var heading = (-GameMath.DirectionToHeading(new Vector3(forX, forY, forZ)) + 360) % 360;

                    // Disregard the duplicates that are below the highest id
                    if (id == maxValue.ToString().PadLeft(3, '0'))
                    {
                        RecordingMetadata metadata = null;
                        var metadataPath = Path.Combine(recordingsPath, $"{Path.GetFileNameWithoutExtension(file)}.json");
                        if (File.Exists(metadataPath))
                        {
                            var metadataContent = File.ReadAllText(metadataPath);
                            metadata = Json.Parse<RecordingMetadata>(metadataContent);
                        }

                        recordings.Add(Path.GetFileNameWithoutExtension(file), new RecordingListing
                        {
                            StartPosition = new Vector4(pos, heading),
                            Metadata = metadata
                        });
                    }
                }

                return recordings;
            }
            catch (Exception ex)
            {
                ex.ToString().Error();
                return [];
            }
        }

        #endregion

        #region Open menu

        private async Task<bool> OpenMenu([FromSource] Player source)
        {
            if (!API.IsPlayerAceAllowed(source.Handle, $"RecM.openMenu"))
                return false;

            return true;
        }

        #endregion

#endif

        #endregion

        #region Ticks

#if CLIENT

        #region General thread

        private async Task GeneralThread()
        {
            if (ScaleformUI.MenuHandler.IsAnyMenuOpen)
            {
                // I don't want the menu to disable all controls, but I want these ones disabled
                if (!_cinematicCamBlocked)
                {
                    _cinematicCamBlocked = true;
                    API.SetCinematicButtonActive(false);
                }
                Game.DisableControlThisFrame((int)InputMode.GamePad, Control.MeleeAttackLight);
            }
            else
            {
                // Reset it just in case the client likes it this way
                if (_cinematicCamBlocked)
                {
                    _cinematicCamBlocked = false;
                    API.SetCinematicButtonActive(true);
                }
            }
        }

        #endregion

        #region Recording checker thread

        private static async Task RecordingCheckerThread()
        {
            var sessions = PlaybackSessionManager.Sessions.ToList();
            if (sessions.Count == 0)
            {
                Main.Instance.DetachTick(RecordingCheckerThread);
                _isPlaybackCheckerAttached = false;
                return;
            }

            List<string> subtitleLines = [];

            foreach (var session in sessions)
            {
                var vehicle = session.Vehicle;
                if (vehicle == null || !vehicle.Exists())
                {
                    if (session.IsPlayerControlled)
                        await StopRecordingPlayback(PlaybackStopReason.Cancelled);
                    else
                        await PlaybackSessionManager.StopSession(session.Id, PlaybackStopReason.Cancelled);
                    continue;
                }

                if (API.IsPlaybackGoingOnForVehicle(vehicle.Handle))
                {
                    string progressText;
                    if (session.IsPlayerControlled)
                    {
                        var playbackSpeed = GetPlaybackSpeedValue();
                        var durationText = TimeSpan.FromMilliseconds(session.Duration).ToString(@"mm\:ss");
                        if (playbackSpeed == 0)
                        {
                            progressText = "Paused";
                        }
                        else
                        {
                            var currentValue = TimeSpan.FromMilliseconds((session.PlaybackStartReference - Game.GameTime) * playbackSpeed).ToString(@"mm\:ss");
                            var prefix = playbackSpeed < 0 && (session.PlaybackStartReference - Game.GameTime) <= 0 ? "00:00" : currentValue;
                            progressText = $"{prefix} / {durationText}";
                        }

                        if (session.Positions.Count > 1)
                        {
                            for (int i = 0; i < session.Positions.Count - 1; i++)
                                World.DrawLine(session.Positions[i], session.Positions[i + 1], Color.FromArgb(255, 0, 0));
                        }

                        if (Game.PlayerPed.IsInVehicle() && Game.PlayerPed.CurrentVehicle != null && Game.PlayerPed.CurrentVehicle == vehicle && Game.PlayerPed.CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver) == Game.PlayerPed)
                        {
                            API.SetPlayerControl(Game.Player.Handle, false, 260);
                            Game.DisableControlThisFrame(0, Control.VehicleExit);
                            if (Game.IsDisabledControlJustReleased(0, Control.VehicleExit))
                                "~r~You can't exit your vehicle whilst recording!".Help(5000);
                        }
                    }
                    else
                    {
                        var currentPosition = TimeSpan.FromMilliseconds(Math.Max(0, API.GetTimePositionInRecording(vehicle.Handle))).ToString(@"mm\:ss");
                        var duration = TimeSpan.FromMilliseconds(session.Duration).ToString(@"mm\:ss");
                        progressText = $"{currentPosition} / {duration}";
                    }

                    var label = session.IsPlayerControlled ? "~b~Player~s~" : "~y~Autonomous~s~";
                    var displayName = string.IsNullOrWhiteSpace(session.DisplayName) ? session.RecordingName : session.DisplayName;
                    subtitleLines.Add($"{label} {displayName}: {progressText}");
                }
                else
                {
                    if (session.IsPlayerControlled)
                        await StopRecordingPlayback(PlaybackStopReason.Completed);
                    else
                        await PlaybackSessionManager.StopSession(session.Id, PlaybackStopReason.Completed);
                }
            }

            if (subtitleLines.Count > 0)
                Screen.ShowSubtitle(string.Join("\n", subtitleLines), 0);
            else
                Screen.HideSubtitleThisFrame();
        }

        #endregion

        #region Recording playback thread

        private static async Task RecordingPlaybackThread()
        {
            RecordThisFrame();
            await BaseScript.Delay(100);
        }

        #endregion

        #region Recording playback checker thread

        private static async Task RecordingPlaybackCheckerThread()
        {
            // Disable vehicle exit
            Game.DisableControlThisFrame(0, Control.VehicleExit);
            if (Game.IsDisabledControlJustReleased(0, Control.VehicleExit))
                "~r~You can't exit your vehicle whilst recording!".Help(5000);
        }

        #endregion

#endif

        #endregion

        #region Tools

#if CLIENT

        #region Start recording

        public static void StartRecording()
        {
            IsRecording = true; 
            Main.Instance.AttachTick(RecordingPlaybackThread);
            Main.Instance.AttachTick(RecordingPlaybackCheckerThread);
        }

        #endregion

        #region Stop recording

        public static void StopRecording()
        {
            IsRecording = false;
            Main.Instance.DetachTick(RecordingPlaybackThread);
            Main.Instance.DetachTick(RecordingPlaybackCheckerThread);
        }

        #endregion

        #region Discard recording

        public static void DiscardRecording()
        {
            IsRecording = false;
            _currRecording.Clear();
            _recordingStartTime = 0;
        }

        #endregion

        #region Save recording

        private static RecordingMetadata CaptureRecordingMetadata(Vehicle vehicle, Ped driver)
        {
            if (vehicle == null || !vehicle.Exists())
                return null;

            RecordingMetadata metadata = new()
            {
                Vehicle = new VehicleMetadata(),
                Driver = null
            };

            var vehicleData = metadata.Vehicle;

            vehicleData.PlateText = API.GetVehicleNumberPlateText(vehicle.Handle);
            vehicleData.PlateStyle = API.GetVehicleNumberPlateTextIndex(vehicle.Handle);
            vehicleData.WindowTint = API.GetVehicleWindowTint(vehicle.Handle);
            vehicleData.WheelType = API.GetVehicleWheelType(vehicle.Handle);

            int primaryColor = 0, secondaryColor = 0;
            API.GetVehicleColours(vehicle.Handle, ref primaryColor, ref secondaryColor);
            vehicleData.PrimaryColor = primaryColor;
            vehicleData.SecondaryColor = secondaryColor;

            int pearlescentColor = 0, wheelColor = 0;
            API.GetVehicleExtraColours(vehicle.Handle, ref pearlescentColor, ref wheelColor);
            vehicleData.PearlescentColor = pearlescentColor;
            vehicleData.WheelColor = wheelColor;

            int dashboardColor = -1;
            API.GetVehicleDashboardColour(vehicle.Handle, ref dashboardColor);
            vehicleData.DashboardColor = dashboardColor;

            int interiorColor = -1;
            API.GetVehicleInteriorColour(vehicle.Handle, ref interiorColor);
            vehicleData.InteriorColor = interiorColor;

            vehicleData.HasCustomPrimaryColor = API.GetIsVehiclePrimaryColourCustom(vehicle.Handle);
            if (vehicleData.HasCustomPrimaryColor)
            {
                int r = 0, g = 0, b = 0;
                API.GetVehicleCustomPrimaryColour(vehicle.Handle, ref r, ref g, ref b);
                vehicleData.CustomPrimaryColor = [r, g, b];
            }

            vehicleData.HasCustomSecondaryColor = API.GetIsVehicleSecondaryColourCustom(vehicle.Handle);
            if (vehicleData.HasCustomSecondaryColor)
            {
                int r = 0, g = 0, b = 0;
                API.GetVehicleCustomSecondaryColour(vehicle.Handle, ref r, ref g, ref b);
                vehicleData.CustomSecondaryColor = [r, g, b];
            }

            vehicleData.Livery = API.GetVehicleLivery(vehicle.Handle);
            vehicleData.RoofLivery = API.GetVehicleRoofLivery(vehicle.Handle);

            bool[] neonEnabled = new bool[4];
            for (int i = 0; i < neonEnabled.Length; i++)
                neonEnabled[i] = API.IsVehicleNeonLightEnabled(vehicle.Handle, i);
            vehicleData.NeonEnabled = neonEnabled;

            int neonR = 0, neonG = 0, neonB = 0;
            API.GetVehicleNeonLightsColour(vehicle.Handle, ref neonR, ref neonG, ref neonB);
            vehicleData.NeonColor = [neonR, neonG, neonB];

            for (int extraId = 0; extraId <= 20; extraId++)
            {
                if (API.DoesExtraExist(vehicle.Handle, extraId))
                    vehicleData.Extras[extraId] = API.IsVehicleExtraTurnedOn(vehicle.Handle, extraId);
            }

            // Ensure the mod kit is set so the data can be read properly
            API.SetVehicleModKit(vehicle.Handle, 0);

            for (int modType = 0; modType <= 49; modType++)
            {
                int available = API.GetNumVehicleMods(vehicle.Handle, modType);
                if (available <= 0 && API.GetVehicleMod(vehicle.Handle, modType) == -1)
                    continue;

                int modIndex = API.GetVehicleMod(vehicle.Handle, modType);
                vehicleData.Mods[modType] = modIndex;

                bool hasVariation = API.GetVehicleModVariation(vehicle.Handle, modType);
                if (hasVariation)
                    vehicleData.ModVariations[modType] = true;
            }

            int[] toggleMods = [17, 18, 19, 20, 21, 22];
            foreach (var toggleMod in toggleMods)
                vehicleData.ModToggles[toggleMod] = API.IsToggleModOn(vehicle.Handle, toggleMod);

            if (driver != null && driver.Exists())
            {
                PedMetadata pedMetadata = new()
                {
                    Model = (uint)driver.Model.Hash
                };

                for (int component = 0; component < 12; component++)
                {
                    pedMetadata.Components.Add(new PedComponentMetadata
                    {
                        Id = component,
                        Drawable = API.GetPedDrawableVariation(driver.Handle, component),
                        Texture = API.GetPedTextureVariation(driver.Handle, component),
                        Palette = API.GetPedPaletteVariation(driver.Handle, component)
                    });
                }

                for (int prop = 0; prop < 8; prop++)
                {
                    int drawable = API.GetPedPropIndex(driver.Handle, prop);
                    int texture = drawable >= 0 ? API.GetPedPropTextureIndex(driver.Handle, prop) : 0;

                    pedMetadata.Props.Add(new PedPropMetadata
                    {
                        Id = prop,
                        Drawable = drawable,
                        Texture = texture
                    });
                }

                metadata.Driver = pedMetadata;
            }

            return metadata;
        }

        private static void ApplyVehicleMetadata(Vehicle vehicle, RecordingMetadata metadata)
        {
            if (vehicle == null || !vehicle.Exists() || metadata?.Vehicle == null)
                return;

            var vehicleData = metadata.Vehicle;

            API.SetVehicleModKit(vehicle.Handle, 0);

            if (!string.IsNullOrEmpty(vehicleData.PlateText))
                API.SetVehicleNumberPlateText(vehicle.Handle, vehicleData.PlateText);
            API.SetVehicleNumberPlateTextIndex(vehicle.Handle, vehicleData.PlateStyle);

            API.SetVehicleWheelType(vehicle.Handle, vehicleData.WheelType);
            API.SetVehicleWindowTint(vehicle.Handle, vehicleData.WindowTint);

            API.SetVehicleColours(vehicle.Handle, vehicleData.PrimaryColor, vehicleData.SecondaryColor);
            API.SetVehicleExtraColours(vehicle.Handle, vehicleData.PearlescentColor, vehicleData.WheelColor);

            if (vehicleData.DashboardColor is int recordedDashboardColor && recordedDashboardColor != -1)
                API.SetVehicleDashboardColour(vehicle.Handle, recordedDashboardColor);

            if (vehicleData.InteriorColor is int recordedInteriorColor && recordedInteriorColor != -1)
                API.SetVehicleInteriorColour(vehicle.Handle, recordedInteriorColor);

            if (vehicleData.HasCustomPrimaryColor && vehicleData.CustomPrimaryColor?.Length == 3)
                API.SetVehicleCustomPrimaryColour(vehicle.Handle, vehicleData.CustomPrimaryColor[0], vehicleData.CustomPrimaryColor[1], vehicleData.CustomPrimaryColor[2]);
            else
                API.ClearVehicleCustomPrimaryColour(vehicle.Handle);

            if (vehicleData.HasCustomSecondaryColor && vehicleData.CustomSecondaryColor?.Length == 3)
                API.SetVehicleCustomSecondaryColour(vehicle.Handle, vehicleData.CustomSecondaryColor[0], vehicleData.CustomSecondaryColor[1], vehicleData.CustomSecondaryColor[2]);
            else
                API.ClearVehicleCustomSecondaryColour(vehicle.Handle);

            if (vehicleData.Livery.HasValue && vehicleData.Livery.Value >= 0)
                API.SetVehicleLivery(vehicle.Handle, vehicleData.Livery.Value);

            if (vehicleData.RoofLivery.HasValue && vehicleData.RoofLivery.Value >= 0)
                API.SetVehicleRoofLivery(vehicle.Handle, vehicleData.RoofLivery.Value);

            if (vehicleData.NeonColor?.Length == 3)
                API.SetVehicleNeonLightsColour(vehicle.Handle, vehicleData.NeonColor[0], vehicleData.NeonColor[1], vehicleData.NeonColor[2]);

            if (vehicleData.NeonEnabled != null)
            {
                for (int i = 0; i < vehicleData.NeonEnabled.Length; i++)
                    API.SetVehicleNeonLightEnabled(vehicle.Handle, i, vehicleData.NeonEnabled[i]);
            }

            if (vehicleData.Extras != null)
            {
                foreach (var extra in vehicleData.Extras)
                    API.SetVehicleExtra(vehicle.Handle, extra.Key, !extra.Value);
            }

            if (vehicleData.Mods != null)
            {
                foreach (var mod in vehicleData.Mods)
                {
                    if (mod.Value >= 0)
                    {
                        bool variation = vehicleData.ModVariations != null && vehicleData.ModVariations.TryGetValue(mod.Key, out bool hasVariation) && hasVariation;
                        API.SetVehicleMod(vehicle.Handle, mod.Key, mod.Value, variation);
                    }
                    else
                    {
                        API.RemoveVehicleMod(vehicle.Handle, mod.Key);
                    }
                }
            }

            if (vehicleData.ModToggles != null)
            {
                foreach (var toggle in vehicleData.ModToggles)
                    API.ToggleVehicleMod(vehicle.Handle, toggle.Key, toggle.Value);
            }
        }

        internal static void ApplyPedMetadata(Ped ped, PedMetadata metadata)
        {
            if (ped == null || !ped.Exists() || metadata == null)
                return;

            if (metadata.Components != null)
            {
                foreach (var component in metadata.Components)
                    API.SetPedComponentVariation(ped.Handle, component.Id, component.Drawable, component.Texture, component.Palette);
            }

            if (metadata.Props != null)
            {
                foreach (var prop in metadata.Props)
                {
                    if (prop.Drawable < 0)
                        API.ClearPedProp(ped.Handle, prop.Id);
                    else
                        API.SetPedPropIndex(ped.Handle, prop.Id, prop.Drawable, prop.Texture, true);
                }
            }
        }

        public static async Task<bool> SaveRecording(string name, bool overwrite = false)
        {
            if (_currRecording.Count == 0)
            {
                "You need to record something first!".Error(true);
                return false;
            }
            if (!Game.PlayerPed.IsInVehicle()) return false;
            if (Game.PlayerPed.CurrentVehicle == null) return false;
            if (!Game.PlayerPed.CurrentVehicle.Exists()) return false;
            if (Game.PlayerPed.CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver) != Game.PlayerPed) return false;
            var veh = Game.PlayerPed.CurrentVehicle;

            // Create a TaskCompletionSource to await the event completion
            var tcs = new TaskCompletionSource<bool>();

            var metadata = CaptureRecordingMetadata(veh, Game.PlayerPed);
            var metadataJson = Json.Stringify(metadata) ?? string.Empty;

            // Latent event that sends little increments of data to the server
            BaseScript.TriggerLatentServerEvent("RecM:saveRecording:Server", 200000, name, veh.DisplayName, Json.Stringify(_currRecording), metadataJson, overwrite, new Action<bool, string>((success, msg) =>
            {
                if (!success)
                {
                    msg.Error(true);
                    tcs.SetResult(false);
                    return;
                }

                // Notify the client of the recording being saved
                "Recording saved!".Log(true);

                // Reset the recording data
                IsRecording = false;
                _currRecording.Clear();
                _recordingStartTime = 0;

                tcs.SetResult(true);
            }));

            return await tcs.Task;
        }

        #endregion

        #region Record this frame

        public static void RecordThisFrame()
        {
            if (!Game.PlayerPed.IsInVehicle()) return;
            if (Game.PlayerPed.CurrentVehicle == null) return;
            if (!Game.PlayerPed.CurrentVehicle.Exists()) return;
            if (Game.PlayerPed.CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver) != Game.PlayerPed) return;
            var veh = Game.PlayerPed.CurrentVehicle;

            // We only need this once per recording, it's the first frame
            if (_recordingStartTime == 0)
                _recordingStartTime = Game.GameTime;

            // Get the entity matrix
            Vector3 forward = new();
            Vector3 right = new();
            Vector3 _ = new();
            Vector3 pos = new();
            API.GetEntityMatrix(veh.Handle, ref forward, ref right, ref _, ref pos);

            // Get the steering angle
            var steeringAngle = (float)(API.GetVehicleSteeringAngle(veh.Handle) * Math.PI / 180);

            // Get the forward motion plus have the brake input combined so the reverse lights can work
            var forwardMotion = API.GetEntitySpeedVector(veh.Handle, true).Y < -1f ? API.GetControlNormal(0, (int)Control.VehicleAccelerate) - API.GetControlNormal(0, (int)Control.VehicleBrake) : API.GetControlNormal(0, (int)Control.VehicleAccelerate);

            // Finally add the recording to the list
            _currRecording.Add(new Record()
            {
                Time = Game.GameTime - _recordingStartTime,
                Position = pos,
                Velocity = veh.Velocity,
                Right = right,
                Forward = forward,
                SteeringAngle = steeringAngle,
                Gas = forwardMotion,
                Brake = API.GetControlNormal(0, (int)Control.VehicleBrake),
                UseHandbrake = API.GetVehicleHandbrake(veh.Handle)
            });
        }

        #endregion

        #region Get recordings

        public static async Task<Tuple<List<string>, Dictionary<string, RecordingListing>>> GetRecordings()
        {
            // Declare the list that will hold the recordings
            List<string> vanilla = [];

            // Now let's load the vanilla recordings
            var json = API.LoadResourceFile("RecM_records", "vanilla.json");
            if (string.IsNullOrEmpty(json))
            {
                vanilla = [];
            }
            else
            {
                foreach (var item in Json.Parse<JArray>(json))
                    vanilla.Add(item.ToString());
            }

            // Get the list of custom recordings
            var custom = await EventDispatcher.Get<Dictionary<string, RecordingListing>>("RecM:getRecordings:Server");

            return new Tuple<List<string>, Dictionary<string, RecordingListing>>(vanilla, custom);
        }

        #endregion

        #region Play recording

        public async static Task<PlaybackSession> PlayRecording(int id, string name, string model = null, Vector4? pos = null, bool takeControl = true, bool spawnDummyDriver = false, string displayName = null, RecordingMetadata metadata = null)
        {
            PlaybackSession session = null;
            try
            {
                if (_recordingCooldown)
                {
                    "Just a 1 second cooldown, please wait...".Warning(true);
                    return null;
                }

                // Just an indicator that the recording is playing
                IsLoadingRecording = true;

                Cooldown();

                // Whether the playback is switching to another playback
                bool isSwitching = false;

                Vehicle veh = null;
                if (takeControl)
                {
                    Vehicle backupVeh = null;
                    if (Game.PlayerPed.IsInVehicle() && Game.PlayerPed.CurrentVehicle != null)
                    {
                        backupVeh = Game.PlayerPed.CurrentVehicle;

                        // Stop the playback if it's going on
                        if (API.IsPlaybackGoingOnForVehicle(backupVeh.Handle))
                            isSwitching = true;
                        else
                            _lastVehicleModel = backupVeh.DisplayName;
                    }

                    if (model != null)
                    {
                        // Check if the model exists
                        if (API.IsModelInCdimage(Game.GenerateHashASCII(model)))
                        {
                            if (backupVeh != null)
                            {
                                // Basically checking if the player's already using the vehicle required for the recording, if so, just use that
                                if (backupVeh.DisplayName == model)
                                    veh = backupVeh;
                                else
                                    veh = await Tools.SpawnVehicle(model, false);
                            }
                            else
                                veh = await Tools.SpawnVehicle(model, false);
                        }
                        else
                        {
                            // Since the model doesn't exist, we'll use the backup vehicle if it exists, otherwise we'll use the default vehicle
                            if (backupVeh != null)
                            {
                                $"The model {model} for this recording doesn't exist, using your current vehicle instead...".Error(true);
                                veh = backupVeh;
                            }
                            else
                            {
                                $"The model {model} for this recording doesn't exist, using the default vehicle instead...".Error(true);
                                veh = await Tools.SpawnVehicle(_defaultVehicle, false);
                            }
                        }
                    }
                    else
                    {
                        if (Game.PlayerPed.IsInVehicle() && Game.PlayerPed.CurrentVehicle != null)
                        {
                            veh = Game.PlayerPed.CurrentVehicle;

                            // Stop the playback if it's going on
                            if (API.IsPlaybackGoingOnForVehicle(veh.Handle))
                                isSwitching = true;
                            else
                                _lastVehicleModel = veh.DisplayName;
                        }
                        else
                            veh = await Tools.SpawnVehicle(_defaultVehicle, false);
                    }
                }
                else
                {
                    string spawnModel = model;
                    if (spawnModel != null && !API.IsModelInCdimage(Game.GenerateHashASCII(spawnModel)))
                    {
                        $"The model {spawnModel} for this recording doesn't exist, using the default vehicle instead...".Error(true);
                        spawnModel = null;
                    }

                    if (string.IsNullOrEmpty(spawnModel))
                        spawnModel = _defaultVehicle;

                    var spawnLocation = pos ?? new Vector4(Game.PlayerPed.Position, Game.PlayerPed.Heading);
                    veh = await PlaybackSessionManager.CreatePlaybackVehicle(spawnModel, spawnLocation, true);

                    if (veh != null && pos != null)
                    {
                        veh.Position = (Vector3)pos;
                        veh.Heading = pos.Value.W;
                    }
                }

                // This actually should be rare but just in case
                if (veh == null)
                {
                    "The vehicle failed to spawn for the recording.".Error(true);
                    IsLoadingRecording = false;
                    return null;
                }

                // Switch the playback if there's already one going on
                if (isSwitching)
                    await SwitchRecordingPlayback();

                // Attach the recording checker tick
                if (!_isPlaybackCheckerAttached)
                {
                    Main.Instance.AttachTick(RecordingCheckerThread);
                    _isPlaybackCheckerAttached = true;
                }

                ApplyVehicleMetadata(veh, metadata);

                // Load the recording with a function call, because the FiveM native doesn't take the name parameter
                API.RequestVehicleRecording(id, name);
                var currTime = Main.GameTime;
                while (!Function.Call<bool>(Hash.HAS_VEHICLE_RECORDING_BEEN_LOADED, id, name) && Main.GameTime - currTime < 7000) // With a timeout of 7 seconds
                    await BaseScript.Delay(1000);

                // It might not have loaded, so let's stop here
                if (!Function.Call<bool>(Hash.HAS_VEHICLE_RECORDING_BEEN_LOADED, id, name))
                {
                    IsLoadingRecording = false;
                    "The recording failed to load.".Error(true);
                    if (!PlaybackSessionManager.HasSessions)
                    {
                        Main.Instance.DetachTick(RecordingCheckerThread);
                        _isPlaybackCheckerAttached = false;
                    }
                    return;
                }

                // Save the player's last position only if the last recording has stopped playing or the last location hasn't been stored
                if (takeControl && (!API.IsPlaybackGoingOnForVehicle(veh.Handle) || _lastLocation == null))
                {
                    // If switching to another playback, we don't want this resetting mid sequence
                    if (!isSwitching)
                        _lastLocation = new Vector4(veh.Position, veh.Heading);
                }

                // Now, teleport the player ONLY if there's given coords (which is mostly likely from the custom recordings)
                if (takeControl && pos != null)
                    await Tools.Teleport((Vector3)pos, pos.Value.W, false);

                // Play the recording
                API.StartPlaybackRecordedVehicle(veh.Handle, id, name, true);

                // I have no idea what it does, but it's in that other yvr recorder script
                API.SetVehicleActiveDuringPlayback(veh.Handle, true);

                session = new PlaybackSession(id, name, takeControl, model, pos, displayName);
                session.Vehicle = veh;
                session.PlaybackStartReference = Game.GameTime;
                session.Duration = Function.Call<float>(Hash.GET_TOTAL_DURATION_OF_VEHICLE_RECORDING, id, name);

                // Store the positions every 120ms of the recording
                for (float time = 0; time <= session.Duration; time += 120)
                {
                    Vector3 position = API.GetPositionOfVehicleRecordingAtTime(id, time, name);
                    session.Positions.Add(position);
                }

                if (!takeControl && spawnDummyDriver)
                    session.DummyDriver = await PlaybackSessionManager.CreateDummyDriver(session.Vehicle, metadata);

                PlaybackSessionManager.AddSession(session);

                if (takeControl)
                {
                    if (!isSwitching)
                        _lastVehicleModel = veh.DisplayName;
                    SwitchPlaybackSpeed(_currPlaybackSpeedIndex);
                }

                // Reset things
                IsLoadingRecording = false;

                return session;
            }
            catch (Exception ex)
            {
                if (session != null && PlaybackSessionManager.GetSession(session.Id) != null)
                    await PlaybackSessionManager.StopSession(session.Id, PlaybackStopReason.Cancelled);
                Clean();
                $"The recording failed to load, check the f8 console for the exception.".Error(true);
                ex.ToString().Error();
            }
            finally
            {
                IsLoadingRecording = false;
            }

            return null;
        }

        #endregion

        #region Switch recording playback

        public static async Task SwitchRecordingPlayback()
        {
            var session = PlaybackSessionManager.PlayerSession;
            if (session == null)
                return;

            await PlaybackSessionManager.StopSession(session.Id, PlaybackStopReason.Switching);
        }

        #endregion

        #region Delete recording

        public static async Task<bool> DeleteRecording(string name, string model)
        {
            // Latent event that sends little increments of data to the server
            (bool success, string msg) = await EventDispatcher.Get<Tuple<bool, string>>("RecM:deleteRecording:Server", name, model);
            if (!success)
            {
                msg.Error(true);
                return false;
            }

            // Notify the client of the recording being deleted
            $"Recording {name} successfully deleted!".Log(true);

            return true;
        }

        #endregion

        #region Stop recording playback

        public async static Task StopRecordingPlayback(PlaybackStopReason reason = PlaybackStopReason.Cancelled)
        {
            var session = PlaybackSessionManager.PlayerSession;
            if (session == null)
            {
                "There's no recording being played at this moment.".Error(true);
                return;
            }

            var vehicle = session.Vehicle;

            if (vehicle != null && vehicle.Exists() && API.IsPlaybackGoingOnForVehicle(vehicle.Handle))
                API.StopPlaybackRecordedVehicle(vehicle.Handle);

            await PlaybackSessionManager.StopSession(session.Id, reason);

            if (reason == PlaybackStopReason.Switching)
                return;

            if (vehicle != null && vehicle.Exists() && _lastVehicleModel != null && _lastVehicleModel != vehicle.DisplayName)
                await Tools.SpawnVehicle(_lastVehicleModel, true);

            if (_lastLocation != null)
                await Tools.Teleport((Vector3)_lastLocation, _lastLocation.Value.W, false);

            API.SetPlayerControl(Game.Player.Handle, true, 0);

            _lastLocation = null;
            _lastVehicleModel = null;

            if (!PlaybackSessionManager.HasSessions)
            {
                Main.Instance.DetachTick(RecordingCheckerThread);
                _isPlaybackCheckerAttached = false;
            }

            "Recording stopped!".Log(true);
        }

        #endregion

        #region Get playback speed list

        public static List<float> GetPlaybackSpeedValueList() => _playbackSpeeds;

        #endregion

        #region Get playback speed list names

        public static List<string> GetPlaybackSpeedNameList() => _playbackSpeeds.Select(x => $"{x}x").ToList();

        #endregion

        #region Get playback speed index

        public static int GetPlaybackSpeedIndex() => _currPlaybackSpeedIndex;

        #endregion

        #region Get playback speed value

        public static float GetPlaybackSpeedValue() => _playbackSpeeds[_currPlaybackSpeedIndex];

        #endregion

        #region Get playback speed name

        public static string GetPlaybackSpeedName() => $"{_playbackSpeeds[_currPlaybackSpeedIndex]}x";

        #endregion

        #region Switch playback speed

        public static void SwitchPlaybackSpeed(int index)
        {
            _currPlaybackSpeedIndex = index;
            if (_currPlaybackSpeedIndex >= _playbackSpeeds.Count)
            {
                _currPlaybackSpeedIndex = _playbackSpeeds.Count - 1;
                return;
            }
            else if (_currPlaybackSpeedIndex < 0)
            {
                _currPlaybackSpeedIndex = 0;
                return;
            }
            string speedName = GetPlaybackSpeedName();
            float speedValue = GetPlaybackSpeedValue();
            var session = PlaybackSessionManager.PlayerSession;
            if (session?.Vehicle != null && session.Vehicle.Exists())
            {
                API.SetPlaybackSpeed(session.Vehicle.Handle, speedValue);
                var divisor = speedValue == 0 ? 1 : speedValue;
                var posInRecording = API.GetTimePositionInRecording(session.Vehicle.Handle) / divisor;
                session.PlaybackStartReference = Game.GameTime - posInRecording;
            }
            RecordingManager.SwitchPlaybackSpeedDisplayBtn.Text = $"Speed {speedName}";
            ScaleformUI.Main.InstructionalButtons.ForceUpdate();
        }


        #endregion

        #region Clean

        private static void Clean()
        {
            var session = PlaybackSessionManager.PlayerSession;
            if (session?.Vehicle == null || !session.Vehicle.Exists())
            {
                _lastLocation = null;
                _lastVehicleModel = null;
                return;
            }

            if (!API.IsPlaybackGoingOnForVehicle(session.Vehicle.Handle))
            {
                _lastLocation = null;
                _lastVehicleModel = null;
            }
        }

        #endregion

        #region Cooldown

        private static async void Cooldown()
        {
            _recordingCooldown = true;
            await BaseScript.Delay(1000);
            _recordingCooldown = false;
        }

        #endregion

        #region Load textures

        /// <summary>
        /// Loads the textures that are supplied with the resource.
        /// </summary>
        private async void LoadTextures()
        {
            var currTime = Main.GameTime;
            while (!API.HasStreamedTextureDictLoaded("recm_textures") && Main.GameTime - currTime < 7000) // With a timeout of 7 seconds
            {
                API.RequestStreamedTextureDict("recm_textures", false);
                await BaseScript.Delay(0);
            }
        }

        #endregion

#endif

#if SERVER

        #region Clean recordings

        /// <summary>
        /// Mostly for the overwrites, since when overwriting, it'll create a duplicate with a higher recording id, so we can use this to reset them back to the lowest id.
        /// </summary>
        private static void CleanRecordings()
        {
            try
            {
                // If the resource doesn't exist
                if (!Directory.Exists(API.GetResourcePath("RecM_records")))
                {
                    "You need to have the RecM_records resource installed.".Error();
                    return;
                }

                // The path to the recordings
                var recordingsPath = Path.Combine(API.GetResourcePath("RecM_records"), "stream");

                foreach (var file in Directory.EnumerateFiles(recordingsPath, "*.yvr"))
                {
                    // Just to be safe
                    if (!Path.GetFileNameWithoutExtension(file).Contains("_"))
                        continue;

                    var name = Path.GetFileNameWithoutExtension(file).Split('_')[0];
                    var model = Path.GetFileNameWithoutExtension(file).Split('_')[1];
                    var id = Path.GetFileNameWithoutExtension(file).Split('_')[2];
                    var maxValue = Directory.EnumerateFiles(recordingsPath, "*.yvr")
                        .Where(x => Path.GetFileNameWithoutExtension(x).StartsWith($"{name}_{model}"))
                        .Select(x => int.Parse(Path.GetFileNameWithoutExtension(x).Split('_')[2]))
                        .DefaultIfEmpty(0)
                        .Max();

                    var metadataPath = Path.Combine(Path.GetDirectoryName(file), $"{Path.GetFileNameWithoutExtension(file)}.json");

                    // Delete the file if there's a duplicate with a lower recording id
                    if (id != maxValue.ToString().PadLeft(3, '0'))
                    {
                        File.Delete(file);
                        if (File.Exists(metadataPath))
                            File.Delete(metadataPath);
                        continue;
                    }

                    // Otherwise, rename the file to the lowest recording id value
                    var targetBaseName = $"{name}_{model}_001";
                    var targetFile = Path.Combine(Path.GetDirectoryName(file), $"{targetBaseName}.yvr");
                    if (!file.Equals(targetFile, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(targetFile))
                            File.Delete(targetFile);

                        File.Move(file, targetFile);
                    }

                    if (File.Exists(metadataPath))
                    {
                        var targetMetadataPath = Path.Combine(Path.GetDirectoryName(metadataPath), $"{targetBaseName}.json");
                        if (!metadataPath.Equals(targetMetadataPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(targetMetadataPath))
                                File.Delete(targetMetadataPath);

                            File.Move(metadataPath, targetMetadataPath);
                        }
                    }
                }

                // Finally, refresh and ensure the recordings resource
                API.ExecuteCommand("refresh");
                API.ExecuteCommand("ensure RecM_records");
            }
            catch (Exception ex)
            {
                ex.ToString().Error();
            }
        }

        #endregion

#endif

        #endregion
    }
}
