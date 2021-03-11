using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Dynastream.Fit;
using GTA;
using GTA.Math;
using GTA.UI;

namespace ActivityGhosts
{
    public class ActivityGhosts : Script
    {
        public static List<Ghost> ghosts;
        private System.DateTime lastTime;
        private Keys loadKey;
        public static PointF initialGPSPoint;
        public static bool debug;

        public ActivityGhosts()
        {
            ghosts = new List<Ghost>();
            lastTime = System.DateTime.UtcNow;
            LoadSettings();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAbort;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (System.DateTime.UtcNow >= lastTime.AddSeconds(1))
            {
                foreach (Ghost g in ghosts)
                    g.Update();
                lastTime = System.DateTime.UtcNow;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == loadKey)
                LoadGhosts();
        }

        private void OnAbort(object sender, EventArgs e)
        {
            KeyDown -= OnKeyDown;
            Tick -= OnTick;
            DeleteAll();
        }

        private void DeleteAll()
        {
            foreach (Ghost g in ghosts)
                g.Delete();
            ghosts.Clear();
        }

        private void LoadGhosts()
        {
            DeleteAll();
            string ghostsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Rockstar Games\\GTA V\\Ghosts";
            if (Directory.Exists(ghostsPath))
            {
                DirectoryInfo dir = new DirectoryInfo(ghostsPath);
                FileInfo[] files = dir.GetFiles("*.fit");
                foreach (FileInfo file in files)
                {
                    FitActivityDecoder fit = new FitActivityDecoder(file.FullName);
                    List<GeoPoint> points = fit.pointList;
                    if (points.Count > 1)
                    {
                        if (Game.Player.Character.Position.DistanceTo2D(new Vector2(points[0].Lat, points[0].Long)) < 50f)
                        {
                            ghosts.Add(new Ghost(points));
                            if (debug) Log($"Loaded ghost from {file.Name}");
                        }
                    }
                }
            }
            Notification.Show($"{ghosts.Count} ghosts loaded");
        }

        private void LoadSettings()
        {
            CultureInfo.CurrentCulture = new CultureInfo("", false);
            ScriptSettings settings = ScriptSettings.Load(@".\Scripts\ActivityGhosts.ini");
            loadKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Main", "LoadGhostsKey", "F8"), true);
            float initialGPSPointLat = settings.GetValue("Main", "InitialGPSPointLat", -19.106371f);
            float initialGPSPointLong = settings.GetValue("Main", "InitialGPSPointLong", -169.870977f);
            initialGPSPoint = new PointF(initialGPSPointLat, initialGPSPointLong);
            debug = settings.GetValue("Main", "Debug", false);
        }

        public static void Log(string message)
        {
            using (StreamWriter sw = new StreamWriter(@".\Scripts\ActivityGhosts.log", true))
                sw.WriteLine(message);
        }
    }

    public class Ghost
    {
        private List<GeoPoint> points;
        private Vehicle vehicle;
        private Ped ped;
        private Blip blip;
        private int index;
        private int skipped;

        private VehicleDrivingFlags customDrivingStyle = VehicleDrivingFlags.AllowGoingWrongWay |
                                                         VehicleDrivingFlags.AllowMedianCrossing |
                                                         VehicleDrivingFlags.AvoidEmptyVehicles |
                                                         VehicleDrivingFlags.AvoidObjects |
                                                         VehicleDrivingFlags.AvoidVehicles;

        private List<string> availableBicycles = new List<string> { "BMX",
                                                                    "CRUISER",
                                                                    "FIXTER",
                                                                    "SCORCHER",
                                                                    "TRIBIKE",
                                                                    "TRIBIKE2",
                                                                    "TRIBIKE3" };

        public Ghost(List<GeoPoint> pointList)
        {
            points = pointList;
            index = 0;
            skipped = 0;
            Model vModel;
            Random random = new Random();
            vModel = new Model(availableBicycles[random.Next(availableBicycles.Count)]);
            vModel.Request();
            if (vModel.IsInCdImage && vModel.IsValid)
            {
                while (!vModel.IsLoaded)
                    Script.Wait(10);
                Vector3 start = GetPoint(index, ActivityGhosts.ghosts.Count + 1);
                vehicle = World.CreateVehicle(vModel, start);
                vModel.MarkAsNoLongerNeeded();
                vehicle.Mods.CustomPrimaryColor = Color.FromArgb(random.Next(255), random.Next(255), random.Next(255));
                Model pModel;
                pModel = new Model("a_m_y_cyclist_01");
                pModel.Request();
                if (pModel.IsInCdImage && pModel.IsValid)
                {
                    while (!pModel.IsLoaded)
                        Script.Wait(10);
                    ped = World.CreatePed(pModel, start);
                    pModel.MarkAsNoLongerNeeded();
                    ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    vehicle.Heading = GetHeading(index);
                    blip = vehicle.AddBlip();
                    blip.Name = "Ghost";
                    blip.Color = BlipColor.Blue;
                }
            }
        }

        public void Update()
        {
            int next = index + 1;
            if (points.Count > next)
            {
                if (vehicle.Position.DistanceTo2D(GetPoint(index)) < 20f)
                {
                    ped.Task.ClearAll();
                    ped.Task.DriveTo(vehicle, GetPoint(next), 0f, points[index].Speed, (DrivingStyle)customDrivingStyle);
                    vehicle.Speed = points[index].Speed;
                    index++;
                    skipped = 0;
                }
                else
                {
                    skipped++;
                    if (ActivityGhosts.debug) ActivityGhosts.Log($"Skipped {skipped} at {index}");
                }
                if (skipped > 4 && points.Count > next + skipped + 1)
                {
                    next += skipped;
                    vehicle.Position = GetPoint(next);
                    vehicle.Heading = GetHeading(next);
                    if (ActivityGhosts.debug) ActivityGhosts.Log($"Teleported from {index} to {next}");
                    ped.Task.ClearAll();
                    ped.Task.DriveTo(vehicle, GetPoint(next + 1), 0f, points[next].Speed, (DrivingStyle)customDrivingStyle);
                    vehicle.Speed = points[next].Speed;
                    index += skipped + 1;
                    skipped = 0;
                }
                ped.IsInvincible = true;
                ped.CanBeKnockedOffBike = true;
            }
            else
                Delete();
        }

        private Vector3 GetPoint(int i, int offset = 0)
        {
            return new Vector3(points[i].Lat + offset, points[i].Long + offset, World.GetGroundHeight(new Vector2(points[i].Lat, points[i].Long)));
        }

        private float GetHeading(int i)
        {
            return (new Vector2(points[i + 1].Lat, points[i + 1].Long) - new Vector2(points[i].Lat, points[i].Long)).ToHeading();
        }

        public void Delete()
        {
            blip.Delete();
            ped.Delete();
            vehicle.Delete();
            points.Clear();
        }
    }

    public class GeoPoint
    {
        public float Lat;
        public float Long;
        public float Speed;

        public GeoPoint(float lat, float lon, float speed)
        {
            Lat = lat;
            Long = lon;
            Speed = speed;
        }
    }

    public class FitActivityDecoder
    {
        public List<GeoPoint> pointList;

        public FitActivityDecoder(string fileName)
        {
            pointList = new List<GeoPoint>();
            var fitSource = new FileStream(fileName, FileMode.Open);
            using (fitSource)
            {
                Decode decode = new Decode();
                MesgBroadcaster mesgBroadcaster = new MesgBroadcaster();
                decode.MesgEvent += mesgBroadcaster.OnMesg;
                decode.MesgDefinitionEvent += mesgBroadcaster.OnMesgDefinition;
                mesgBroadcaster.RecordMesgEvent += OnRecordMessage;
                bool status = decode.IsFIT(fitSource);
                status &= decode.CheckIntegrity(fitSource);
                if (status)
                    decode.Read(fitSource);
                fitSource.Close();
            }
        }

        private void OnRecordMessage(object sender, MesgEventArgs e)
        {
            var recordMessage = (RecordMesg)e.mesg;
            float s = recordMessage.GetSpeed() == null ? 0 : (float)recordMessage.GetSpeed();
            if (s > 0)
            {
                PointF from = new PointF(SemicirclesToDeg(recordMessage.GetPositionLat()), SemicirclesToDeg(recordMessage.GetPositionLong()));
                double dist = Distance(from, ActivityGhosts.initialGPSPoint);
                double bearing = -1 * Bearing(from, ActivityGhosts.initialGPSPoint);
                pointList.Add(new GeoPoint((float)(dist * Math.Cos(bearing)), (float)(dist * Math.Sin(bearing)), s));
            }
        }

        private double Distance(PointF from, PointF to)
        {
            double dLat = (DegToRad(to.X) - DegToRad(from.X));
            double dLon = (DegToRad(to.Y) - DegToRad(from.Y));
            double latFrom = DegToRad(from.X);
            double latTo = DegToRad(to.X);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(latFrom) * Math.Cos(latTo) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 6378137.0f;
        }

        private double Bearing(PointF from, PointF to)
        {
            double dLon = (DegToRad(to.Y) - DegToRad(from.Y));
            double latFrom = DegToRad(from.X);
            double latTo = DegToRad(to.X);
            double y = Math.Sin(dLon) * Math.Cos(latTo);
            double x = Math.Cos(latFrom) * Math.Sin(latTo) -
                       Math.Sin(latFrom) * Math.Cos(latTo) * Math.Cos(dLon);
            return Math.Atan2(y, x) + (Math.PI / 2);
        }

        private float SemicirclesToDeg(int? angleSemi)
        {
            return (float)(angleSemi * (180.0f / int.MaxValue));
        }

        private double DegToRad(float angleDeg)
        {
            return angleDeg * Math.PI / 180.0f;
        }
    }
}
