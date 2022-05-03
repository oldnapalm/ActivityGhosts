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
using GTA.Native;
using GTA.UI;
using LemonUI;
using LemonUI.Menus;

namespace ActivityGhosts
{
    public class ActivityGhosts : Script
    {
        private readonly List<Ghost> ghosts;
        private Blip start;
        private int lastTime;
        private Keys menuKey;
        private Keys loadKey;
        public static PointF initialGPSPoint;
        public static int opacity;
        private bool showDate;
        private ObjectPool menuPool;
        private NativeMenu mainMenu;
        private NativeItem loadMenuItem;
        private NativeItem regroupMenuItem;
        private NativeItem deleteMenuItem;

        public ActivityGhosts()
        {
            ghosts = new List<Ghost>();
            lastTime = Environment.TickCount;
            LoadSettings();
            CreateMenu();
            Tick += OnTick;
            Aborted += OnAbort;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Environment.TickCount >= lastTime + 1000)
            {
                foreach (Ghost g in ghosts)
                    g.Update();
                lastTime = Environment.TickCount;
            }

            if (showDate)
                foreach (Ghost g in ghosts)
                    if (g.ped.IsOnScreen && g.ped.IsInRange(Game.Player.Character.Position, 20f))
                    {
                        var pos = g.ped.Bones[Bone.IKHead].Position + new Vector3(0, 0, 0.5f) + g.ped.Velocity / Game.FPS;
                        Function.Call(Hash.SET_DRAW_ORIGIN, pos.X, pos.Y, pos.Z, 0);
                        g.date.Scale = 0.4f - GameplayCamera.Position.DistanceTo(g.ped.Position) * 0.01f;
                        g.date.Draw();
                        Function.Call(Hash.CLEAR_DRAW_ORIGIN);
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
            lastTime = Environment.TickCount;
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
                    if (points.Count > 1 && Game.Player.Character.Position.DistanceTo2D(new Vector2(points[0].Lat, points[0].Long)) < 50f)
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
                        string span;
                        var seconds = (System.DateTime.UtcNow - fit.startTime).TotalSeconds;
                        if (seconds < 7200) span = $"{seconds / 60:N0} minutes";
                        else if (seconds < 172800) span = $"{seconds / 3600:N0} hours";
                        else if (seconds < 1209600) span = $"{seconds / 86400:N0} days";
                        else if (seconds < 5259492) span = $"{seconds / 604800:N0} weeks";
                        else span = $"{seconds / 2629746:N0} months";
                        ghosts.Add(new Ghost(points, fit.sport, span));
                    }
                }
                if (ghosts.Count > 0)
                {
                    start = World.CreateBlip(Game.Player.Character.Position);
                    start.Sprite = BlipSprite.RaceBike;
                    loadMenuItem.Enabled = false;
                    regroupMenuItem.Enabled = true;
                    deleteMenuItem.Enabled = true;
                }
            }
            Notification.Show($"{ghosts.Count} ghosts loaded");
        }

        private void LoadSettings()
        {
            CultureInfo.CurrentCulture = new CultureInfo("", false);
            ScriptSettings settings = ScriptSettings.Load(@".\Scripts\ActivityGhosts.ini");
            menuKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Main", "MenuKey", "F8"), true);
            loadKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Main", "LoadKey", "G"), true);
            float initialGPSPointLat = settings.GetValue("Main", "InitialGPSPointLat", -19.10637f);
            float initialGPSPointLong = settings.GetValue("Main", "InitialGPSPointLong", -169.871f);
            initialGPSPoint = new PointF(initialGPSPointLat, initialGPSPointLong);
            opacity = settings.GetValue("Main", "Opacity", 5);
            if (opacity < 1) opacity = 1;
            if (opacity > 5) opacity = 5;
            opacity *= 51;
            showDate = settings.GetValue("Main", "ShowDate", true);
        }

        private void CreateMenu()
        {
            menuPool = new ObjectPool();
            mainMenu = new NativeMenu("ActivityGhosts");
            menuPool.Add(mainMenu);
            loadMenuItem = new NativeItem("Load", "Load ghosts")
            {
                Enabled = true
            };
            mainMenu.Add(loadMenuItem);
            loadMenuItem.Activated += (sender, itemArgs) =>
            {
                LoadGhosts();
                mainMenu.Visible = false;
            };
            regroupMenuItem = new NativeItem("Regroup", "Regroup ghosts")
            {
                Enabled = false
            };
            mainMenu.Add(regroupMenuItem);
            regroupMenuItem.Activated += (sender, itemArgs) =>
            {
                RegroupGhosts();
                mainMenu.Visible = false;
            };
            deleteMenuItem = new NativeItem("Delete", "Delete ghosts")
            {
                Enabled = false
            };
            mainMenu.Add(deleteMenuItem);
            deleteMenuItem.Activated += (sender, itemArgs) =>
            {
                DeleteGhosts();
                loadMenuItem.Enabled = true;
                regroupMenuItem.Enabled = false;
                deleteMenuItem.Enabled = false;
                mainMenu.Visible = false;
            };
            menuPool.RefreshAll();
            Tick += (o, e) => menuPool.Process();
            KeyDown += (o, e) =>
            {
                if (e.KeyCode == menuKey)
                    mainMenu.Visible = !mainMenu.Visible;
                else if (e.KeyCode == loadKey)
                {
                    if (ghosts.Count == 0)
                        LoadGhosts();
                    else
                        RegroupGhosts();
                }
            };
        }
    }

    public class Ghost
    {
        private readonly List<GeoPoint> points;
        private readonly Sport sport;
        private readonly Vehicle vehicle;
        public Ped ped;
        public TextElement date;
        private readonly Blip blip;
        private int index = 0;
        private bool finished = false;
        private readonly Animation animation = new Animation();
        private readonly Animation lastAnimation = new Animation();

        private readonly VehicleDrivingFlags customDrivingStyle = VehicleDrivingFlags.AllowGoingWrongWay |
                                                                  VehicleDrivingFlags.AllowMedianCrossing |
                                                                  VehicleDrivingFlags.AvoidEmptyVehicles |
                                                                  VehicleDrivingFlags.AvoidObjects |
                                                                  VehicleDrivingFlags.AvoidPeds |
                                                                  VehicleDrivingFlags.AvoidVehicles |
                                                                  VehicleDrivingFlags.IgnorePathFinding;

        private readonly string[] availableBicycles = { "BMX", "CRUISER", "FIXTER", "SCORCHER", "TRIBIKE", "TRIBIKE2", "TRIBIKE3" };

        private readonly string[] availableCyclists = { "a_m_y_cyclist_01", "a_m_y_roadcyc_01" };

        private readonly string[] availableRunners = { "a_m_y_runner_01", "a_m_y_runner_02" };

        public Ghost(List<GeoPoint> pointList, Sport type, string span)
        {
            points = pointList;
            sport = type;
            Random random = new Random();
            Vector3 start = GetPoint(index);
            if (sport == Sport.Cycling)
            {
                Model vModel;
                vModel = new Model(availableBicycles[random.Next(availableBicycles.Length)]);
                vModel.Request();
                if (vModel.IsInCdImage && vModel.IsValid)
                {
                    while (!vModel.IsLoaded)
                        Script.Wait(10);
                    vehicle = World.CreateVehicle(vModel, start);
                    vModel.MarkAsNoLongerNeeded();
                    vehicle.IsInvincible = true;
                    vehicle.Opacity = ActivityGhosts.opacity;
                    vehicle.Mods.CustomPrimaryColor = Color.FromArgb(random.Next(255), random.Next(255), random.Next(255));
                }
            }
            Model pModel;
            pModel = sport == Sport.Cycling ? new Model(availableCyclists[random.Next(availableCyclists.Length)]) :
                new Model(availableRunners[random.Next(availableRunners.Length)]);
            pModel.Request();
            if (pModel.IsInCdImage && pModel.IsValid)
            {
                while (!pModel.IsLoaded)
                    Script.Wait(10);
                ped = World.CreatePed(pModel, start);
                pModel.MarkAsNoLongerNeeded();
                ped.IsInvincible = true;
                ped.Opacity = ActivityGhosts.opacity;
                if (sport == Sport.Cycling)
                {
                    ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    vehicle.Heading = GetHeading(index);
                }
                else
                    ped.Heading = GetHeading(index);
                blip = ped.AddBlip();
                blip.Sprite = BlipSprite.Ghost;
                blip.Name = "Ghost (active)";
                blip.Color = BlipColor.WhiteNotPure;
            }
            date = new TextElement($"{span} ago", new PointF(0, 0), 1f, Color.WhiteSmoke, GTA.UI.Font.ChaletLondon, Alignment.Center, false, true);
        }

        public void Update()
        {
            if (points.Count > index + 1)
            {
                float speed = points[index].Speed;
                if (sport == Sport.Cycling)
                {
                    if (!ped.IsInVehicle(vehicle))
                        ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
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
                else
                {
                    float distance = ped.Position.DistanceTo2D(GetPoint(index));
                    if (distance > 10f)
                    {
                        ped.Position = GetPoint(index);
                        ped.Heading = GetHeading(index);
                    }
                    else if (distance > 3f)
                        speed *= 1.1f;
                    index++;
                    ped.Task.GoTo(GetPoint(index));
                    SetAnimation(speed);
                    ped.Speed = speed;
                }
            }
            else if (!finished)
            {
                finished = true;
                ped.Task.ClearAll();
                if (sport == Sport.Cycling && ped.IsInVehicle(vehicle))
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
                if (finished)
                {
                    finished = false;
                    blip.Name = "Ghost (active)";
                    blip.Color = BlipColor.WhiteNotPure;
                }
                if (sport == Sport.Cycling)
                {
                    if (!ped.IsInVehicle(vehicle))
                        ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    vehicle.Position = GetPoint(index);
                    vehicle.Heading = GetHeading(index);
                    ped.Task.ClearAll();
                    ped.Task.DriveTo(vehicle, GetPoint(index + 1), 0f, points[index].Speed, (DrivingStyle)customDrivingStyle);
                    vehicle.Speed = points[index].Speed;
                }
                else
                {
                    ped.Position = GetPoint(index);
                    ped.Heading = GetHeading(index);
                    ped.Task.GoTo(GetPoint(index + 1));
                    SetAnimation(points[index].Speed);
                    ped.Speed = points[index].Speed;
                }
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
            vehicle?.Delete();
            points.Clear();
        }

        private class Animation
        {
            public string dictionary;
            public string name;
            public float speed;

            public Animation()
            {
                dictionary = "";
                name = "";
                speed = 0.0f;
            }

            public bool IsEmpty()
            {
                return dictionary == "" && name == "" && speed == 0.0f;
            }
        }

        private void SetAnimation(float speed)
        {
            if (speed < 2.4f)
            {
                animation.dictionary = "move_m@casual@f";
                animation.name = "walk";
                animation.speed = speed / 1.69f;
            }
            else if (speed >= 2.4f && speed < 4.6f)
            {
                animation.dictionary = "move_m@jog@";
                animation.name = "run";
                animation.speed = speed / 3.13f;
            }
            else if (speed >= 4.6f)
            {
                animation.dictionary = "move_m@gangster@generic";
                animation.name = "sprint";
                animation.speed = speed / 6.63f;
            }
            if (animation.name != lastAnimation.name || ped.Speed == 0)
            {
                if (!lastAnimation.IsEmpty())
                    ped.Task.ClearAnimation(lastAnimation.dictionary, lastAnimation.name);
                ped.Task.PlayAnimation(animation.dictionary, animation.name, 8.0f, -8.0f, -1,
                    AnimationFlags.Loop | AnimationFlags.AllowRotation, animation.speed);
                lastAnimation.dictionary = animation.dictionary;
                lastAnimation.name = animation.name;
            }
            Function.Call(Hash.SET_ENTITY_ANIM_SPEED, ped, animation.dictionary, animation.name, animation.speed);
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
        public System.DateTime startTime;
        public Sport sport = Sport.Cycling;

        public FitActivityDecoder(string fileName)
        {
            pointList = new List<GeoPoint>();
            startTime = new FileInfo(fileName).CreationTime;
            var fitSource = new FileStream(fileName, FileMode.Open);
            using (fitSource)
            {
                Decode decode = new Decode();
                MesgBroadcaster mesgBroadcaster = new MesgBroadcaster();
                decode.MesgEvent += mesgBroadcaster.OnMesg;
                decode.MesgDefinitionEvent += mesgBroadcaster.OnMesgDefinition;
                mesgBroadcaster.RecordMesgEvent += OnRecordMessage;
                mesgBroadcaster.SessionMesgEvent += OnSessionMessage;
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
            float s = recordMessage.GetSpeed() ?? 0f;
            if (s > 0f)
            {
                PointF from = new PointF(SemicirclesToDeg(recordMessage.GetPositionLat()), SemicirclesToDeg(recordMessage.GetPositionLong()));
                double dist = Distance(from, ActivityGhosts.initialGPSPoint);
                double bearing = -1 * Bearing(from, ActivityGhosts.initialGPSPoint);
                pointList.Add(new GeoPoint((float)(dist * Math.Cos(bearing)), (float)(dist * Math.Sin(bearing)), s));
            }
        }

        private void OnSessionMessage(object sender, MesgEventArgs e)
        {
            var sessionMessage = (SessionMesg)e.mesg;
            startTime = sessionMessage.GetStartTime().GetDateTime();
            sport = sessionMessage.GetSport() ?? Sport.Cycling;
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
