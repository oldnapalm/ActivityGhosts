using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dynastream.Fit;
using GTA;
using GTA.Math;
using GTA.UI;
using NativeUI;

namespace ActivityGhosts
{
    public class ActivityGhosts : Script
    {
        private List<Ghost> ghosts;
        private Blip start;
        private System.DateTime lastTime;
        private Keys menuKey;
        public static PointF initialGPSPoint;
        public static int opacity;

        public ActivityGhosts()
        {
            ghosts = new List<Ghost>();
            lastTime = System.DateTime.UtcNow;
            LoadSettings();
            CreateMenu();
            Tick += OnTick;
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

        private void OnAbort(object sender, EventArgs e)
        {
            Tick -= OnTick;
            DeleteGhosts();
        }

        private void DeleteGhosts()
        {
            foreach (Ghost g in ghosts)
                g.Delete();
            ghosts.Clear();
            start?.Delete();
        }

        private void RegroupGhosts()
        {
            foreach (Ghost g in ghosts)
                g.Regroup(new PointF(Game.Player.Character.Position.X, Game.Player.Character.Position.Y));
            lastTime = System.DateTime.UtcNow;
        }

        private void LoadGhosts()
        {
            string activitiesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Rockstar Games\\GTA V\\Activities";
            if (Directory.Exists(activitiesPath))
            {
                DirectoryInfo dir = new DirectoryInfo(activitiesPath);
                FileInfo[] files = dir.GetFiles("*.fit");
                foreach (FileInfo file in files)
                {
                    FitActivityDecoder fit = new FitActivityDecoder(file.FullName);
                    List<GeoPoint> points = fit.pointList;
                    if (points.Count > 1)
                    {
                        if (Game.Player.Character.Position.DistanceTo2D(new Vector2(points[0].Lat, points[0].Long)) < 50f)
                        {
                            int offset = ghosts.Count / 2 + 1;
                            if (ghosts.Count % 2 == 0)
                                offset *= -1;
                            points[0].Lat += offset;
                            float h = Game.Player.Character.Heading;
                            if ((h > 90f && h < 180f) || (h > 270f && h < 360f))
                                points[0].Long -= offset;
                            else
                                points[0].Long += offset;
                            ghosts.Add(new Ghost(points));
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
            menuKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Main", "MenuKey", "F8"), true);
            float initialGPSPointLat = settings.GetValue("Main", "InitialGPSPointLat", -19.10637f);
            float initialGPSPointLong = settings.GetValue("Main", "InitialGPSPointLong", -169.871f);
            initialGPSPoint = new PointF(initialGPSPointLat, initialGPSPointLong);
            opacity = settings.GetValue("Main", "Opacity", 50);
            if (opacity < 0) opacity = 0;
            if (opacity > 100) opacity = 100;
            opacity *= 255 / 100;
        }

        private void CreateMenu()
        {
            var menuPool = new MenuPool();
            var mainMenu = new UIMenu("ActivityGhosts", "Ride with ghosts from previous activities");
            menuPool.Add(mainMenu);
            var loadMenuItem = new UIMenuItem("Load", "Load ghosts");
            loadMenuItem.Enabled = true;
            mainMenu.AddItem(loadMenuItem);
            var regroupMenuItem = new UIMenuItem("Regroup", "Regroup ghosts");
            regroupMenuItem.Enabled = false;
            mainMenu.AddItem(regroupMenuItem);
            var deleteMenuItem = new UIMenuItem("Delete", "Delete ghosts");
            deleteMenuItem.Enabled = false;
            mainMenu.AddItem(deleteMenuItem);
            mainMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == loadMenuItem && loadMenuItem.Enabled)
                {
                    LoadGhosts();
                    if (ghosts.Count > 0)
                    {
                        start = World.CreateBlip(Game.Player.Character.Position);
                        start.Sprite = BlipSprite.RaceBike;
                        loadMenuItem.Enabled = false;
                        regroupMenuItem.Enabled = true;
                        deleteMenuItem.Enabled = true;
                    }
                }
                else if (item == regroupMenuItem && regroupMenuItem.Enabled)
                    RegroupGhosts();
                else if (item == deleteMenuItem && deleteMenuItem.Enabled)
                {
                    DeleteGhosts();
                    loadMenuItem.Enabled = true;
                    regroupMenuItem.Enabled = false;
                    deleteMenuItem.Enabled = false;
                }
                mainMenu.Visible = false;
            };
            menuPool.RefreshIndex();
            Tick += (o, e) => menuPool.ProcessMenus();
            KeyDown += (o, e) =>
            {
                if (e.KeyCode == menuKey)
                    mainMenu.Visible = !mainMenu.Visible;
            };
        }
    }

    public class Ghost
    {
        private List<GeoPoint> points;
        private Vehicle vehicle;
        private Ped ped;
        private Blip blip;
        private int index;

        private VehicleDrivingFlags customDrivingStyle = VehicleDrivingFlags.AllowGoingWrongWay |
                                                         VehicleDrivingFlags.AllowMedianCrossing |
                                                         VehicleDrivingFlags.AvoidEmptyVehicles |
                                                         VehicleDrivingFlags.AvoidObjects |
                                                         VehicleDrivingFlags.AvoidPeds |
                                                         VehicleDrivingFlags.AvoidVehicles |
                                                         VehicleDrivingFlags.IgnorePathFinding;

        private string[] availableBicycles = { "BMX", "CRUISER", "FIXTER", "SCORCHER", "TRIBIKE", "TRIBIKE2", "TRIBIKE3" };

        private string[] availableCyclists = { "a_m_y_cyclist_01", "a_m_y_roadcyc_01" };

        public Ghost(List<GeoPoint> pointList)
        {
            points = pointList;
            index = 0;
            Model vModel;
            Random random = new Random();
            vModel = new Model(availableBicycles[random.Next(availableBicycles.Length)]);
            vModel.Request();
            if (vModel.IsInCdImage && vModel.IsValid)
            {
                while (!vModel.IsLoaded)
                    Script.Wait(10);
                Vector3 start = GetPoint(index);
                vehicle = World.CreateVehicle(vModel, start);
                vModel.MarkAsNoLongerNeeded();
                vehicle.IsInvincible = true;
                vehicle.Opacity = ActivityGhosts.opacity;
                vehicle.Mods.CustomPrimaryColor = Color.FromArgb(random.Next(255), random.Next(255), random.Next(255));
                Model pModel;
                pModel = new Model(availableCyclists[random.Next(availableCyclists.Length)]);
                pModel.Request();
                if (pModel.IsInCdImage && pModel.IsValid)
                {
                    while (!pModel.IsLoaded)
                        Script.Wait(10);
                    ped = World.CreatePed(pModel, start);
                    pModel.MarkAsNoLongerNeeded();
                    ped.IsInvincible = true;
                    ped.Opacity = ActivityGhosts.opacity;
                    ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    vehicle.Heading = GetHeading(index);
                    blip = vehicle.AddBlip();
                    blip.Sprite = BlipSprite.Ghost;
                    blip.Name = "Ghost (active)";
                    blip.Color = BlipColor.WhiteNotPure;
                }
            }
        }

        public void Update()
        {
            if (points.Count > index + 1)
            {
                if (!ped.IsInVehicle(vehicle))
                    ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                float speed = points[index].Speed;
                float distance = vehicle.Position.DistanceTo2D(GetPoint(index));
                if (distance > 20f)
                {
                    vehicle.Position = GetPoint(index);
                    vehicle.Heading = GetHeading(index);
                }
                else if (distance > 5f)
                    speed *= 1.1f;
                index++;
                ped.Task.ClearAll();
                ped.Task.DriveTo(vehicle, GetPoint(index), 0f, speed, (DrivingStyle)customDrivingStyle);
                vehicle.Speed = speed;
            }
            else if (ped.IsInVehicle(vehicle))
            {
                ped.Task.ClearAll();
                ped.Task.LeaveVehicle(vehicle, false);
                blip.Name = "Ghost (finished)";
                blip.Color = BlipColor.Red;
            }
        }

        public void Regroup(PointF point)
        {
            index = points.IndexOf(points.OrderBy(x => Distance(point, x)).First());
            if (points.Count > index + 1)
            {
                if (!ped.IsInVehicle(vehicle))
                {
                    ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    blip.Name = "Ghost (active)";
                    blip.Color = BlipColor.WhiteNotPure;
                }
                vehicle.Position = GetPoint(index);
                vehicle.Heading = GetHeading(index);
                ped.Task.ClearAll();
                ped.Task.DriveTo(vehicle, GetPoint(index + 1), 0f, points[index].Speed, (DrivingStyle)customDrivingStyle);
                vehicle.Speed = points[index].Speed;
                index++;
            }
        }

        private double Distance(PointF from, GeoPoint to)
        {
            return Math.Sqrt((to.Long - from.Y) * (to.Long - from.Y) + (to.Lat - from.X) * (to.Lat - from.X));
        }

        private Vector3 GetPoint(int i)
        {
            return new Vector3(points[i].Lat, points[i].Long, World.GetGroundHeight(new Vector2(points[i].Lat, points[i].Long)));
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
