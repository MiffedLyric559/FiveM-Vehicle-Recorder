using System.Collections.Generic;
using CitizenFX.Core;

namespace RecM
{
    public class RecordingMetadata
    {
        public VehicleMetadata Vehicle { get; set; }
        public PedMetadata Driver { get; set; }
    }

    public class VehicleMetadata
    {
        public Dictionary<int, int> Mods { get; set; } = new();
        public Dictionary<int, bool> ModToggles { get; set; } = new();
        public Dictionary<int, bool> Extras { get; set; } = new();
        public Dictionary<int, bool> ModVariations { get; set; } = new();
        public int? Livery { get; set; }
        public int? RoofLivery { get; set; }
        public int PrimaryColor { get; set; }
        public int SecondaryColor { get; set; }
        public int PearlescentColor { get; set; }
        public int WheelColor { get; set; }
        public int? DashboardColor { get; set; }
        public int? InteriorColor { get; set; }
        public bool HasCustomPrimaryColor { get; set; }
        public int[] CustomPrimaryColor { get; set; }
        public bool HasCustomSecondaryColor { get; set; }
        public int[] CustomSecondaryColor { get; set; }
        public bool[] NeonEnabled { get; set; }
        public int[] NeonColor { get; set; }
        public int WindowTint { get; set; }
        public int WheelType { get; set; }
        public string PlateText { get; set; }
        public int PlateStyle { get; set; }
    }

    public class PedMetadata
    {
        public uint Model { get; set; }
        public List<PedComponentMetadata> Components { get; set; } = new();
        public List<PedPropMetadata> Props { get; set; } = new();
    }

    public class PedComponentMetadata
    {
        public int Id { get; set; }
        public int Drawable { get; set; }
        public int Texture { get; set; }
        public int Palette { get; set; }
    }

    public class PedPropMetadata
    {
        public int Id { get; set; }
        public int Drawable { get; set; }
        public int Texture { get; set; }
    }

    public class RecordingListing
    {
        public Vector4 StartPosition { get; set; }
        public RecordingMetadata Metadata { get; set; }
    }
}
