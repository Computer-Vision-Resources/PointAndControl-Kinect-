﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;
using PointAndControl.Devices;
using PointAndControl.Kinect;
using PointAndControl.WebServer;
using PointAndControl.ComponentHandling;
using PointAndControl.Helperclasses;
using PointAndControl.ThirdPartyRepos;

namespace PointAndControl.MainComponents
{

    public class leanDevRepresentation
    {
        public String id { get; set; }
        public String name { get; set; }
        public leanDevRepresentation (Device d)
        {
            id = d.Id;
            name = d.Name;
        }
    }
    /// <summary>
    ///     This class takes place of the design pattern fassade. It encapsulates the different subsystems and combines the different interfaces which can be called by the HttpServer.
    ///     The IGS is the central control unit and passes on the tasks.
    ///     Contains the observer for the HttpEvents as well as KinectEvents.
    ///     @author Sven Ochs, Frederik Reiche
    /// </summary>
    public class PointAndControlMain
    {


        /// <summary>
        ///     Constructor for the IGS.
        ///     Among other things it creates a concrete observer for HttpEven and KinectEvent.
        ///     <param name="data">The Dataholder</param>
        ///     <param name="tracker">The Usertracker</param>
        ///     <param name="server">The HTTP server</param>
        /// </summary>
        public PointAndControlMain(DataHolder data, UserTracker tracker, HttpServer server, EventLogger eventLogger)
        {
            environmentHandler = new EnvironmentInfoHandler();
            Data = data;
            Tracker = tracker;
            Server = server;
            Server.postRequest += server_Post_Request;
            Server.Request += server_Request;
            Tracker.KinectEvents += UserLeft;
            Tracker.Strategy.TrackingStateEvents += SwitchTrackingState;


            createIGSKinect();
            json_paramReader = new JSON_ParameterReader();
            this.Transformer = new CoordTransform(IGSKinect.tiltingDegree, IGSKinect.roomOrientation, IGSKinect.ball.Center);
            
            
            logger = eventLogger;
            this.coreMethods = new CollisionMethod(Data, Tracker, Transformer, logger);

          
        }


        /// <summary>
        ///     With the "set"-method the DataHolder can be set.
        ///     With the "get"-method the DataHolder can be returned.
        /// </summary>
        public DataHolder Data { get; set; }


        /// <summary>
        ///     With the "set"-method the UserTracker can be set.
        ///     With the "get"-method the UserTracker can be returned.
        /// </summary>
        public UserTracker Tracker { get; set; }

        /// <summary>
        ///     With the "set"-method the HTTP-Server can be set.
        ///     With the "get"-method the HTTP-Server can be returned.
        /// </summary>
        public HttpServer Server { get; set; }
        /// <summary>
        ///     With the "set"-method the IGSKinect can be set.
        ///     With the "get"-method the IGSKinect can be returned.
        /// </summary>
        public DevKinect IGSKinect { get; set; }

        /// <summary>
        /// Marks if the devices are initialized or not.
        /// With the "set"-method the devInit can be set.
        /// With the "get"-method the devInit can be returned.
        /// </summary>
        public bool devInit { get; set; }

        /// <summary>
        /// With the "set"-method the CoordTransform can be set.
        /// With the "get"-method the CoordTransform can be returned.
        /// </summary>
        public CoordTransform Transformer { get; set; }

 

        ICoreMethods coreMethods { get; set; }

        public EventLogger logger { get; set; }

        public EnvironmentInfoHandler environmentHandler { get; set; }

        public bool isRunning { get; set; }

        public bool cancellationRequest { get; set; }

        private JSON_ParameterReader json_paramReader { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SwitchTrackingState(object sender, TrackingStateEventArgs args)
        {
            if (Data.GetUserBySkeleton(args.SkeletonId) != null)
            {
                Data.GetUserBySkeleton(args.SkeletonId).TrackingState = false;
            }
        }


        /// <summary>
        ///     Part of the design pattern: observer(HttpEvent).
        ///     Takes place for the update-method in the observer design pattern.
        /// </summary>


        private void server_Post_Request(object sender, HttpEventArgs e)
        {
            String str = "";
            Server.SendResponse(e.P, str);
        }
        
        private void server_Request(object sender, HttpEventArgs e)
        {
            Debug.WriteLine("server_Request");
            String str = InterpretCommand(sender, e);
            Server.SendResponse(e.P, str);

            if (cancellationRequest)
                shutDown();
        }
        
        /// <summary>
        ///     Part of the design pattern: observer(KinectEvent).
        ///     Takes place for the update-method in the observer design pattern.
        ///     In the case that a user left the kinects field of view his skeleton ID in his user-object will be deleted and the gesture control deactivated.
        ///     The user will be notified with the next to the server.
        ///     <param name="sender">Object which triggered the event</param>
        ///     <param name="args">The KinectUserEvent with the information about the user</param>
        /// </summary>
        public void UserLeft(object sender, KinectUserEventArgs args)
        {
            User user = Data.GetUserBySkeleton(args.SkeletonId);
            if (user != null)
            {
                user.AddError(Properties.Resources.RoomLeft);
                user.TrackingState = false;
            }
            Data.DelTrackedSkeleton(args.SkeletonId);
        }

        /// <summary>
        ///     This method adds a new user to the DataHolder with his registered wlan adress.
        ///     <param name="wlanAdr">Wlan adress of the new registered user</param>
        /// </summary>
        public bool AddUser(String wlanAdr)
        {
            return Data.AddUser(wlanAdr);
        }

        public string AddUser(String wlanAdr, out bool success)
        {
            return Data.AddUser(wlanAdr, out success);
        }

        /// <summary>
        ///     This method fetches the id of the skeleton from the user currently perfoming the gesture to register.
        ///     This id will be set in the UserObject which is through its WLAN-adress unique.
        ///     If this procedure is finished successfully, the gesture control is for the user active and can be used.
        ///     <param name="wlanAdr">WLAN-Adress of the user wanting to activate gesture control</param>
        /// </summary>
        public int SkeletonIdToUser(String wlanAdr)
        {
            
            User tempUser = Data.GetUserByIp(wlanAdr);
            int id = -1;

            if (tempUser != null)
            {
                id = Tracker.GetSkeletonId(tempUser.SkeletonId);

                if (id >= 0)
                {
                    tempUser.TrackingState = true;
                    Data.SetTrackedSkeleton(wlanAdr, id);
                }
            }

            return id;
        }

        /// <summary>
        ///     Passes the command with the provided ID on to the device.
        ///     <param name="sender">The object which triggered the event.</param>
        ///     <param name="args">Parameter needed for the interpretation.</param>
        /// </summary>
        public String InterpretCommand(object sender, HttpEventArgs args)
        {
            String devId = args.Dev;
            String cmd = args.Cmd;
            String value = args.Val;
            Dictionary<String, String> parameters = json_paramReader.deserializeValueDict(value);
            JsonResponse response = new JsonResponse();
            String wlanAdr = args.ClientIp;
            String lang = args.Language;

            User user = Data.GetUserByIp(wlanAdr);
            Device device = null;
            String retStr = "";
            String msg = "";
            Boolean success = false;

            String paramDevId;



            if (Thread.CurrentThread.CurrentCulture.Name != lang)
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(lang);
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(lang);
            }


            if (cmd != "popup" && cmd != "pollDevice")
                logger.enqueueEntry(String.Format("Command arrived! devID: {0}; cmdID: {1}; value: {2}; wlanAdr: {3}", devId, cmd, value, wlanAdr));

            if (devId == "server")
            {
                // return JSON formatted message
                args.P.WriteSuccess("application/json");


                response.addCmd(cmd);

                switch (cmd)
                {
                    case "addUser":

                        AddUser(wlanAdr, out success);
                        if (Data.GetUserByIp(wlanAdr) != null)
                        {
                            if (Tracker.isKinectAvailable())
                            {
                                response.addTrackingId(SkeletonIdToUser(wlanAdr));
                            } else
                            {
                                msg = Properties.Resources.NoKinAvailable;
                            }
                        }

                        break;

                    case "close":
                        success = DelUser(wlanAdr);
                        if (!success)
                        {
                            msg = Properties.Resources.UserNotExists;
                        }
                        break;

                    case "activateGestureCtrl":
                        if (!Tracker.isKinectAvailable())
                        {
                            msg = Properties.Resources.NoKinAvailable;
                            break;
                        }

                        if (user != null)
                        {
                            int id = SkeletonIdToUser(wlanAdr);

                            if (id >= 0)
                                success = true;
                            else if (id == UserTracker.NO_GESTURE_FOUND)
                                msg = Properties.Resources.NoGestureFound;
                            else if (id == UserTracker.NO_BODIES_IN_FRAME)
                                msg = Properties.Resources.NoUserInImage;

                            // attach tracking state
                            response.addTrackingId(id);
                            break;
                        }

                        if (!success)
                            msg = Properties.Resources.GesturecontrolError;

                        break;

                    case "pollDevice":
                    case "selectDevice":
                        if (!Tracker.isKinectAvailable())
                        {
                            msg = Properties.Resources.NoKinAvailable;
                            break;
                        }

                        if (user == null || !user.TrackingState)
                        {
                            msg = Properties.Resources.RegistrationRequest;
                            break;
                        }

                        List<Device> foundDevices = coreMethods.chooseDevice(user);
                        if (foundDevices.Count > 0)
                            success = true;

                        response.addDevices(foundDevices);
                        break;

                        
                    case "list":


                        Data.updateRepoDevices();
                        response.addDevices(Data.getCompleteDeviceList());
                        success = true;
                        break;

                    case "discoverDevices":
                        success = true;
                        // Data.newDevices = discoverDevices();
                        response.addDevices(Data.newDevices);
                        break;

                    case "addDevice":
                        if (parameters == null)
                        {
                            msg = Properties.Resources.NoValues;
                            break;
                        }

                        msg = AddDevice(parameters);
                        success = true;

                        break;

                    //case "addDeviceFromList":
                    //    // find device in newDevices list
                    //    if (parameters == null)
                    //    {
                    //        msg = Properties.Resources.NoValues;
                    //        break;
                    //    }

                    //    if(!json_paramReader.getDevID(parameters, out paramDevId))
                    //    {
                    //        msg = paramDevId;
                    //        break;
                    //    }

                    //    device = Data.newDevices.Find(d => d.Id.Equals(paramDevId));

                    //    if (device != null)
                    //    {
                    //        success = true;
                    //        if (!json_paramReader.getDevName(parameters, out paramDevName))
                    //        {
                    //            msg = paramDevName;
                    //            break;
                    //        }
                    //        String type = device.GetType().Name;

                    //        String newDeviceId = AddDevice(type, "", paramDevName, device.Path);

                    //        // remove from new devices list
                    //        Data.newDevices.Remove(device);
                    //        response.addDeviceId(newDeviceId);
                    //    }

                    //    break;

                    case "resetDeviceVectorList":

                        if (parameters == null)
                        {
                            msg = Properties.Resources.NoValues;
                            break;
                        }

                        if (!json_paramReader.getDevID(parameters, out paramDevId))
                        {
                            msg = paramDevId;
                            break;
                        }

                        device = Data.getDeviceByID(paramDevId);

                        if (device == null)
                        {
                            msg = Properties.Resources.NoDevFound;
                            break;
                        }

                        success = true;
                        device.skelPositions.Clear();

                        //attach vector numbers
                        response.addVectorMinAndCount(device.skelPositions.Count, coreMethods.getMinVectorsPerDevice());
                        break;

                    case "addDeviceVector":

                        if (parameters == null)
                        {
                            msg = Properties.Resources.NoValues;
                            break;
                        }

                        if (!json_paramReader.getDevID(parameters, out paramDevId))
                        {
                            msg = paramDevId;
                            break;
                        }

                        device = Data.getDeviceByID(paramDevId);

                        if (device == null)
                        {
                            msg = Properties.Resources.NoDevFound;
                            break;
                        }

                        if (user == null || !user.TrackingState)
                        {
                            msg = Properties.Resources.RegistrationRequest;
                            break;
                        }
                        
                        success = true;
                        msg = addDeviceVector(device, user);

                        //attach vector numbers 
                        response.addVectorMinAndCount(device.skelPositions.Count, coreMethods.getMinVectorsPerDevice());
                        break;
                     
                    case "setDevicePosition":

                        if (parameters == null)
                        {
                            msg = Properties.Resources.NoValues;
                            break;
                        }

                        if(!json_paramReader.getDevID(parameters, out paramDevId))
                        {
                            msg = paramDevId;
                            break;
                        }

                        device = Data.getDeviceByID(paramDevId);

                        if (device == null)
                        {
                            msg = Properties.Resources.NoDevFound;
                            break;
                        }

                        success = true;
                        msg = coreMethods.train(device);
                        break;

                    case "deleteDevice":

                        if (!json_paramReader.getDevID(parameters, out paramDevId))
                        {
                            msg = Properties.Resources.DevNotFoundDeletion;
                            break;
                        }

                        device = Data.getDeviceByID(paramDevId);

                        if (device == null)
                        {
                            msg = Properties.Resources.DevNotFoundDeletion;
                            break;
                        }

                        msg = Data.deleteDevice(device.Id);
                        success = true;
                        break;

                    case "popup":
                        if (user != null)
                        {
                            success = true;
                            msg = user.Errors;
                            user.ClearErrors();

                            // attach tracking state
                            response.addTrackingId(user.SkeletonId);
                        }
                        break;
                    //TODO: Already implemented but no GUI-Interface for these functionalities
                    //case "setPlugwisePath":
                    //    success = setPlugwiseComponents(parameters);
                    //    if (success)
                    //    {
                    //        msg = Properties.Resources.PWCompChange;
                    //    } else
                    //    {
                    //        msg = Properties.Resources.UnknownError;
                    //    }
                    //    break;
                    //case "setKinectComponents":
                    //    success = setKinectPositionWithDict(parameters);
                    //    if (success)
                    //    {
                    //        msg = Properties.Resources.KinectPlacementChanged;
                    //    } else
                    //    {
                    //        msg = "";
                    //    }
                    //    break;
                    //case "setRoomMeasures":
                    //    success = setRoomMeasures(parameters);
                    //    if (success)
                    //    {
                    //        msg = Properties.Resources.RoommeasuresChanged;
                    //    } else
                    //    {
                    //        msg = "";
                    //    }
                    //    break;


                    //TODO: Already implemented but no GUI-Interface for these functionalities 
                    case "getDeviceTypes":
                        success = true;
                        response.addDeviceTypes(getDeviceTypeJSON());

                        break;
                }
                response.addSuccess(success);
                response.addMsg(msg);
               
                

                if ((cmd != "popup" || msg != "") && (cmd != "pollDevice"))
                {
                    logger.enqueueEntry(String.Format("Respronse to '{0}' : {1}", cmd, retStr));
                }

                return response.serialize();

            }
            else if (Data.getDeviceByID(devId) != null && cmd != null)
            {
                switch (cmd)
                {
                    case "getControlPath":

                        response.addReturnString(getControlPagePathHttp(devId));

                        args.P.WriteRedirect(response.getReturnString(), 301);
                        break;

                    default:
                        Device dev = Data.getDeviceByID(devId);
                        if (dev.connection != null && NativeTransmittingDevice.checkIfTransmitting(dev))
                        {
                            response.addReturnString(((NativeTransmittingDevice)dev).Transmit(cmd, value));
                        }
                        break;
                }

                logger.enqueueEntry(String.Format("Response to Request {0} : {1} ", cmd, retStr));

                return response.serialize();
            }
            else
            {
                retStr = Properties.Resources.UnknownError;
                logger.enqueueEntry(String.Format("Response to Request {0} : {1}", cmd, retStr));


                response.addReturnString(retStr);

                return response.serialize();
            }
        }


        public static String MakeDeviceString(IEnumerable<Device> devices)
        {
            
            List<leanDevRepresentation> d = new List<leanDevRepresentation>();
            if (devices != null)
            {
                List<Device> deviceList = devices.Where(dev => RepositoryRepresentation.isRepo(dev) == false).ToList();

                deviceList.ForEach(x => d.Add(new leanDevRepresentation(x)));
            }
            
            String result = JsonConvert.SerializeObject(d, Formatting.Indented);

            return result;
        }



        /// <summary>
        ///     This method deletes the user who closed his app.
        ///     <param name="wlanAdr">wlan adress of the user who closed his app</param>
        /// </summary>
        public bool DelUser(String wlanAdr)
        {
            return Data.DelUser(wlanAdr);
        }

        /// <summary>
        ///     Adds a new device to the device list and updates the deviceConfiguration part of the config.xml.
        ///     <param name="parameter">
        ///         Parameter of the device which should be added.
        ///         Parameter: Type, Name, Id, Form, Address
        ///     </param>
        ///     <returns>returns a response string what result the process had</returns>
        /// </summary>
        public String AddDevice(String type, String ID, String name, String path)
        {

            String retStr = "";

            retStr = Data.AddDevice(type,ID, name, path);


            return retStr;
        }

        public String AddDevice(Dictionary<String, String> values)
        {
            String type;
            String name;
            String id;
            String path;
            String retStr = "";


            if (json_paramReader.getDevNameTypePath(values, out type, out name, out path))
            {
                if (json_paramReader.getDevID(values, out id) && !id.Equals(""))
                {
                    retStr = Data.AddDevice(type, id, name, path);
                } else
                {
                    retStr = Data.AddDevice(type, "", name, path);
                }

            }
            else
            {
                retStr = Properties.Resources.AddDeviceError;
            }

            return retStr;
        }

        /// <summary>
        /// this method intializes the representation of the kinect camera used for positioning and 
        /// visualization by reading the information out of the config.xml
        /// </summary>
        public void createIGSKinect()
        {
            float ballRad = 0.4f;

            Point3D kinectCenter = new Point3D(environmentHandler.getKinectPosX(), environmentHandler.getKinectPosY(),environmentHandler.getKinectPosZ());
            Ball kinectBall = new Ball(kinectCenter, ballRad);
            double roomOrientation = environmentHandler.getKinecHorizontalAngle();
            double tiltingDegree = environmentHandler.getKinectTiltAngle();

            
            IGSKinect = new DevKinect("DevKinect", kinectBall, tiltingDegree, roomOrientation);
        }


        public String addDeviceVector(Device dev, User user)
        {
            if (dev == null)
                return Properties.Resources.UnknownDev;
            
            if (Tracker.Bodies.Count == 0)
                return Properties.Resources.NoUserInImage;
            
            if (user.SkeletonId < 0)
                return Properties.Resources.RegistrationRequest;

            Point3D[] vectors = Transformer.transformJointCoords(Tracker.GetCoordinates(user.SkeletonId));

            dev.skelPositions.Add(vectors);

            return String.Format(Properties.Resources.AddDevVec, dev.skelPositions.Count, coreMethods.getMinVectorsPerDevice());

        }

        public String getControlPagePathHttp(String id)
        {
            String controlPath = "";
            Device dev = Data.getDeviceByID(id);
            String t = dev.GetType().Name;

            if (t.Equals("ExternalDevice"))
            {
                controlPath = dev.Path;
            }
            else
            {
                controlPath = "http://" + Server.LocalIP + ":8080" + "/" + t + "/" + "index.html?dev=" + id;
            }

            return controlPath;
        }

        private bool setPlugwiseComponents(Dictionary<string, string> values)
        {
            String host;
            String port;
            String path;

            json_paramReader.getPlugwiseComponents(values, out host, out port, out path);

            Data.change_PlugWise_Adress(host, port, path);

            return true;
        }

        private bool setKinectPositionWithDict(Dictionary<String,String> values)
        {
            String x;
            String y;
            String z;
            String horizontal;
            String tilt;

            json_paramReader.getKinectPosition(values, out x, out y, out z, out horizontal, out tilt);

            return setKinect(x, y, z, horizontal, tilt);

           
        }

        private bool setKinect(String x, String y, String z, String horizontal, String tilt)
        {
            double parsedX; 
            double parsedY;
            double parsedZ;
            double parsedHorizontal;
            double parsedTilt;
            bool changed = false;


            if(Double.TryParse(x, out parsedX) &&
               Double.TryParse(y, out parsedY) &&
               Double.TryParse(z, out parsedZ))
            {
                Point3D newCenter = new Point3D(parsedX, parsedY, parsedZ);

                IGSKinect.setKinectCoords(newCenter);

                Transformer.transVector = (Vector3D)newCenter;
                Data._environmentHandler.setKinectCoordsOnly(parsedX, parsedY, parsedZ);
                changed = true;
            }

            if(Double.TryParse(horizontal, out parsedHorizontal))
            {
                IGSKinect.roomOrientation = parsedHorizontal;
                Data._environmentHandler.setKinectHorizontalAnlge(parsedHorizontal);
                changed = true;
            }

            if(Double.TryParse(tilt, out parsedTilt))
            {
                IGSKinect.tiltingDegree = parsedTilt;
                Data._environmentHandler.setKinectTiltAngle(parsedTilt);
                changed = true;
            }


            if (Tracker.Sensor != null && changed)
            {
                Transformer.calculateRotationMatrix(IGSKinect.tiltingDegree, IGSKinect.roomOrientation);
            }

            logger.enqueueEntry(String.Format("Placement of Kinect Changed| X:{0}, Y:{1}, Z:{2}, Horizontal:{3}, Tilt:{4}", x, y, z, horizontal, tilt));

            return changed;
        }

        private bool setRoomMeasures(Dictionary<String, String> values)
        {
            String width;
            String height;
            String depth;

            json_paramReader.getRoomMeasures(values, out width, out height, out depth);

            Data.changeRoomSizeRemote(width, height, depth);
            logger.enqueueEntry(String.Format("Roomsize Changed| width:{0}, height:{1}, depth:{2}", width, height, depth));

            return true;
        }

        private String getDeviceTypeJSON()
        {
            return JsonConvert.SerializeObject(Device.getAllDerivedDeviceTypesAsStrings().ToArray(), Formatting.Indented);
        }

        public bool shutDown()
        {
            Tracker.ShutDown();
            isRunning = false;

            return true;
        }
    }

}