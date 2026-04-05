using System;
using System.Globalization;
using System.Text;
using ICities;

namespace GeoSkylines
{
    public sealed class GeoSkylinesCenterState
    {
        public string MapName;
        public double CenterLatitude;
        public double CenterLongitude;
        public bool HasCenter;

        public GeoSkylinesCenterState Clone()
        {
            return new GeoSkylinesCenterState
            {
                MapName = MapName,
                CenterLatitude = CenterLatitude,
                CenterLongitude = CenterLongitude,
                HasCenter = HasCenter
            };
        }
    }

    public static class GeoSkylinesSaveState
    {
        private static GeoSkylinesCenterState currentState;

        public static bool TryGetCenterState(out GeoSkylinesCenterState state)
        {
            if (currentState != null && currentState.HasCenter)
            {
                state = currentState.Clone();
                return true;
            }

            state = null;
            return false;
        }

        public static void SetCenterState(string mapName, double centerLatitude, double centerLongitude)
        {
            currentState = new GeoSkylinesCenterState
            {
                MapName = mapName,
                CenterLatitude = centerLatitude,
                CenterLongitude = centerLongitude,
                HasCenter = true
            };
        }

        public static void Clear()
        {
            currentState = null;
        }
    }

    public class GeoSkylinesSerializableDataExtension : SerializableDataExtensionBase
    {
        private const string DataId = "GeoSkylines.CenterState";

        public override void OnCreated(ISerializableData serializableData)
        {
            base.OnCreated(serializableData);
            GeoSkylinesSaveState.Clear();
        }

        public override void OnLoadData()
        {
            base.OnLoadData();
            GeoSkylinesSaveState.Clear();

            byte[] data = serializableDataManager.LoadData(DataId);
            if (data == null || data.Length == 0)
            {
                return;
            }

            string raw = Encoding.UTF8.GetString(data);
            string[] parts = raw.Split(new[] { '\n' }, StringSplitOptions.None);
            if (parts.Length < 3)
            {
                return;
            }

            double centerLatitude;
            double centerLongitude;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out centerLatitude))
            {
                return;
            }

            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out centerLongitude))
            {
                return;
            }

            GeoSkylinesSaveState.SetCenterState(parts[0], centerLatitude, centerLongitude);
        }

        public override void OnSaveData()
        {
            base.OnSaveData();

            GeoSkylinesCenterState state;
            if (!GeoSkylinesSaveState.TryGetCenterState(out state))
            {
                serializableDataManager.EraseData(DataId);
                return;
            }

            string raw = (state.MapName ?? string.Empty) + "\n"
                + state.CenterLatitude.ToString("R", CultureInfo.InvariantCulture) + "\n"
                + state.CenterLongitude.ToString("R", CultureInfo.InvariantCulture);
            serializableDataManager.SaveData(DataId, Encoding.UTF8.GetBytes(raw));
        }

        public override void OnReleased()
        {
            base.OnReleased();
            GeoSkylinesSaveState.Clear();
        }
    }
}
