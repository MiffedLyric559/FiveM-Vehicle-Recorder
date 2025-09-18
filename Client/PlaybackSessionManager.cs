using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using RecM.Client.Utils;

namespace RecM.Client
{
    public enum PlaybackStopReason
    {
        Completed,
        Cancelled,
        Switching
    }

    public class PlaybackSession
    {
        public PlaybackSession(int recordingId, string recordingName, bool isPlayerControlled, string vehicleModel, Vector4? startPosition, string displayName = null)
        {
            Id = Guid.NewGuid();
            RecordingId = recordingId;
            RecordingName = recordingName;
            IsPlayerControlled = isPlayerControlled;
            VehicleModel = vehicleModel;
            StartPosition = startPosition;
            DisplayName = displayName ?? recordingName;
        }

        public Guid Id { get; }

        public int RecordingId { get; }

        public string RecordingName { get; }

        public string DisplayName { get; }

        public bool IsPlayerControlled { get; }

        public string VehicleModel { get; }

        public Vector4? StartPosition { get; }

        public Vehicle Vehicle { get; set; }

        public Ped DummyDriver { get; set; }

        public float PlaybackStartReference { get; set; }

        public float Duration { get; set; }

        public List<Vector3> Positions { get; } = [];
    }

    public static class PlaybackSessionManager
    {
        private static readonly List<PlaybackSession> _sessions = [];

        public static ReadOnlyCollection<PlaybackSession> Sessions => _sessions.AsReadOnly();

        public static PlaybackSession PlayerSession => _sessions.FirstOrDefault(x => x.IsPlayerControlled);

        public static void AddSession(PlaybackSession session)
        {
            if (!_sessions.Contains(session))
                _sessions.Add(session);
        }

        public static PlaybackSession GetSession(Guid sessionId) => _sessions.FirstOrDefault(x => x.Id == sessionId);

        public static bool HasSessions => _sessions.Count > 0;

        public static async Task<Vehicle> CreatePlaybackVehicle(string model, Vector4 spawnLocation, bool networked = true)
        {
            if (string.IsNullOrEmpty(model))
                return null;

            var vehicleModel = new Model(model);
            var modelLoaded = await Tools.LoadModel(vehicleModel, 10);
            if (!modelLoaded)
                return null;

            Vehicle vehicle = null;
            if (networked)
                vehicle = await World.CreateVehicle(vehicleModel, new Vector3(spawnLocation.X, spawnLocation.Y, spawnLocation.Z), spawnLocation.W);
            else
                vehicle = await World.CreateVehicle(vehicleModel, new Vector3(spawnLocation.X, spawnLocation.Y, spawnLocation.Z), spawnLocation.W);

            if (vehicle == null)
                return null;

            vehicle.IsInvincible = true;
            vehicle.CanBeVisiblyDamaged = false;
            vehicle.IsEngineRunning = true;
            vehicle.RadioStation = RadioStation.RadioOff;
            vehicleModel.MarkAsNoLongerNeeded();

            return vehicle;
        }

        public static async Task<Ped> CreateDummyDriver(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return null;

            var pedModel = new Model(PedHash.AirworkerSMY);
            if (!await pedModel.Request(10000))
                return null;

            var ped = await World.CreatePed(pedModel, vehicle.Position, vehicle.Heading);
            if (ped == null)
                return null;

            ped.Task.ClearAllImmediately();
            ped.IsInvincible = true;
            ped.CanWrithe = false;
            ped.Model.MarkAsNoLongerNeeded();
            ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
            ped.BlockPermanentEvents = true;

            return ped;
        }

        public static async Task StopSession(Guid sessionId, PlaybackStopReason reason = PlaybackStopReason.Completed)
        {
            var session = GetSession(sessionId);
            if (session == null)
                return;

            if (session.Vehicle != null && session.Vehicle.Exists())
            {
                if (API.IsPlaybackGoingOnForVehicle(session.Vehicle.Handle))
                    API.StopPlaybackRecordedVehicle(session.Vehicle.Handle);
            }

            API.RemoveVehicleRecording(session.RecordingId, session.RecordingName);

            if (session.DummyDriver != null && session.DummyDriver.Exists())
                session.DummyDriver.Delete();

            if (!session.IsPlayerControlled && session.Vehicle != null && session.Vehicle.Exists())
                session.Vehicle.Delete();

            session.DummyDriver = null;
            session.Vehicle = null;

            _sessions.Remove(session);

            await BaseScript.Delay(0);
        }

        public static string GetStatusText(PlaybackSession session)
        {
            if (session?.Vehicle == null || !session.Vehicle.Exists())
                return "Vehicle not available";

            var current = TimeSpan.FromMilliseconds(API.GetTimePositionInRecording(session.Vehicle.Handle)).ToString(@"mm\:ss");
            var total = TimeSpan.FromMilliseconds(session.Duration).ToString(@"mm\:ss");
            return $"{current} / {total}";
        }
    }
}
