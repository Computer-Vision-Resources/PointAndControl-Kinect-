﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using System.Xml;
using IGS.Server.WebServer;
using IGS.Server.Devices;
using IGS.Server.Kinect;
using System.Diagnostics;
using System.IO;
using IGS.Helperclasses;
using System.Net;
using System.Text;
using IGS.KNN;
using Microsoft.Kinect;
using System.Threading;

namespace IGS.Server.IGS
{
    /// <summary>
    ///     This class takes place of the design pattern fassade. It encapsulates the different subsystems and combines the different interfaces which can be called by the HttpServer.
    ///     The IGS is the central control unit and passes on the tasks.
    ///     Contains the observer for the HttpEvents as well as KinectEvents.
    ///     @author Sven Ochs, Frederik Reiche
    /// </summary>
    public class Igs
    {


        /// <summary>
        ///     Constructor for the IGS.
        ///     Among other things it creates a concrete observer for HttpEven and KinectEvent.
        ///     <param name="data">The Dataholder</param>
        ///     <param name="tracker">The Usertracker</param>
        ///     <param name="server">The HTTP server</param>
        /// </summary>
        public Igs(DataHolder data, UserTracker tracker, HttpServer server)
        {

            Data = data;
            Tracker = tracker;
            Server = server;
            Server.postRequest += server_Post_Request;
            Server.Request += server_Request;
            Tracker.KinectEvents += UserLeft;
            Tracker.Strategy.TrackingStateEvents += SwitchTrackingState;

            learnedOnline = false;

            createIGSKinect();
            

            this.Transformer = new CoordTransform(IGSKinect.tiltingDegree, IGSKinect.roomOrientation, IGSKinect.ball.Centre);
            this.classification = new ClassificationHandler();

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
        public devKinect IGSKinect { get; set; }

        /// <summary>
        /// Marks if the devices are initialized or not.
        /// With the "set"-method the devInit can be set.
        /// With the "get"-method the devInit can be returned.
        /// </summary>
        public bool devInit { get; set; }

        public bool learnedOnline { get; set; }

        /// <summary>
        /// With the "set"-method the CoordTransform can be set.
        /// With the "get"-method the CoordTransform can be returned.
        /// </summary>
        public CoordTransform Transformer { get; set; }

        public ClassificationHandler classification { get; set; }

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
                user.AddError("You left the room!");
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

        /// <summary>
        ///     This method fetches the id of the skeleton from the user currently perfoming the gesture to choose a device.
        ///     This id will be set in the UserObject which is through its WLAN-adress unique.
        ///     If this procedure is finished successfully, the gesture control is for the user active and can be used.
        ///     <param name="wlanAdr">WLAN-Adress of the user wanting to activate gesture control</param>
        /// </summary>
        public bool SkeletonIdToUser(String wlanAdr)
        {
            User tempUser = Data.GetUserByIp(wlanAdr);
            int id = -1;
            if (tempUser != null)
            {
                int sklId = tempUser.SkeletonId;

                id = Tracker.GetSkeletonId(sklId);

                if (id != -1)
                    tempUser.TrackingState = true;
            }

            return id >= 0 && Data.SetTrackedSkeleton(wlanAdr, id);
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
            String wlanAdr = args.ClientIp;
            String retStr = "";
            String msg = "";
            Boolean success = false;
            
            if(cmd != "popup")
                XMLComponentHandler.writeLogEntry("Command arrived! devID: " + devId + " cmdID: " + cmd + " value: " + value + " wlanAdr: " + wlanAdr);           
           
            if (devId == "server")
            {
                // return JSON formatted message
                args.P.WriteSuccess("application/json");
                retStr = "{\"cmd\":\"" + cmd + "\"";


                if (cmd != "addUser" && cmd != "popup")
                {
                    // notify online learner that no control command was sent
                    onlineNoSucces(devId, wlanAdr);
                }

                switch (cmd)
                {
                    case "addUser":
                        success = AddUser(wlanAdr);
                        break;

                    case "close":
                        success = DelUser(wlanAdr);
                        break;

                    case "activateGestureCtrl":
                        if (Data.GetUserByIp(wlanAdr) != null)
                        {
                            success = SkeletonIdToUser(wlanAdr);
                        }

                        if (!success)
                            msg = "Activation of gesture control failed. Please check camera and restart application.";

                        break;

                    case "selectDevice":
                        if (Data.GetUserByIp(wlanAdr).TrackingState)
                        {
                            success = true;
                            retStr += "," + MakeDeviceString(ChooseDevice(wlanAdr));
                            break;
                        }

                        msg = "No device found. Please try again.";
                        break;

                    case "list":
                        success = true;
                        retStr += "," + MakeDeviceString(Data.Devices);
                        break;

                    case "addDevice":
                        string[] parameter = value.Split(':');
                        if (parameter.Length == 4)
                        {
                          success = true;
                          msg = AddDevice(parameter);
                          break;
                        }

                        msg = "Failed to add device. Incorrect number of parameters";
                        break;

                    case "popup":
                        if (Data.GetUserByIp(wlanAdr) != null)
                        {
                            success = true;   
                            msg = Data.GetUserByIp(wlanAdr).Errors;
                            Data.GetUserByIp(wlanAdr).ClearErrors();
                        }
                        break;
                }

                // finalize JSON response
                retStr += ",\"success\":" + success.ToString().ToLower() +",\"msg\":\"" + msg + "\"}";
                Console.WriteLine(retStr);

                if (cmd != "popup" || msg != "")
                {
                    XMLComponentHandler.writeLogEntry("Response to '" + cmd + "': " + retStr);
                }

                return retStr;
            }
            else if (devId != null && Data.getDeviceByID(devId) != null)
            {
                //if (cmdId == "addDeviceCoord")
                //{
                    
                //    retStr = AddDeviceCoord(devId, wlanAdr, value);
                //    XMLComponentHandler.writeLogEntry("Response to 'addDeviceCoord': " + retStr);
                    
                //    return retStr;

                //}
                if (cmd == "getControlPath")
                {
                    onlineNoSucces(devId, wlanAdr);
                    retStr = getControlPagePathHttp(devId);
                    XMLComponentHandler.writeLogEntry("Response to 'getControlPath': " + retStr);
                    // redirect to device control path
                    args.P.WriteRedirect(retStr);

                    return retStr;
                }
                else if (cmd == "collectDeviceSample")
                {
                    Console.WriteLine("collect kam an!" + "Value:" + value);
                    retStr = collectSample(wlanAdr, value);
                    XMLComponentHandler.writeLogEntry("Response to 'collectDeviceSample': " + retStr);

                    return retStr;
                }
                else if (cmd != null)
                {
                    // assumes that correct device was selected
                    executeOnlineLearning(devId, wlanAdr);
                    retStr = Data.getDeviceByID(devId).Transmit(cmd, value);
                    XMLComponentHandler.writeLogEntry("Response to 'control device': " + retStr);
                    return retStr;
                }

            }
            else
            {   
                // TODO: JSON response
                retStr = "Invalid command";
                return retStr;
            }
            return null;
        }


        private String MakeDeviceString(IEnumerable<Device> devices)
        {
            String result = "\"devices\":[";

            if (devices != null)
            {
                Device[] deviceList = devices.ToArray<Device>();
                for (int i = 0; i < deviceList.Length; i++)
                {
                    if (i != 0)
                        result += ",";
                    result += "{\"id\":\"" + deviceList[i].Id + "\", \"name\":\"" + deviceList[i].Name + "\"}";
                }
            }
            result += "]";
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
        ///     Calculates the possible device the user want to choose per gesture control
        ///     <param name="wlanAdr">wlan adress of the user</param>
        ///     <returns>list with the possiible devices</returns>
        /// </summary>
        public List<Device> ChooseDevice(String wlanAdr)
        {
            List<Device> dev = new List<Device>();
            User tempUser = Data.GetUserByIp(wlanAdr);
            Vector3D[] vecs = Transformer.transformJointCoords(Tracker.getMedianFilteredCoordinates(tempUser.SkeletonId));
            //Vector3D[] vecs = Transformer.transformJointCoords(Tracker.GetCoordinates(tempUser.SkeletonId));
            if (tempUser != null)
            {
              
                WallProjectionSample sample = classification.collector.calculateSample(vecs, "");
 
                //String label = collector.calcRoomModel.hitSquareCheck(new Point3D(sample.x, sample.y, sample.z));
                //if (label != null)
                //{
                //    sample.sampleDeviceName = label;


                //    foreach (Device d in Data.Devices)
                //    {
                //        if (d.Name.ToLower() == sample.sampleDeviceName.ToLower())
                //        {
                //            dev.Add(d);
                //            return dev;
                //        }
                //    }
                //}
               
                sample = classification.classify(sample);
            
                Console.WriteLine("Classified: " + sample.sampleDeviceName);
                XMLComponentHandler.writeLogEntry("Device classified to" + sample.sampleDeviceName);
                Body body = Tracker.GetBodyById(tempUser.SkeletonId);
                writeUserJointsToXmlFile(tempUser, Data.GetDeviceByName(sample.sampleDeviceName), body);
                XMLComponentHandler.writeUserJointsPerSelectClick(body);
                classification.deviceClassificationCount++;

                Device device = Data.GetDeviceByName(sample.sampleDeviceName);
                sample.sampleDeviceName = device.Name;

                
              
                if (sample != null)
                {
                    foreach (Device d in Data.Devices)
                    {
                        if (d.Name.ToLower() == sample.sampleDeviceName.ToLower())
                        {
                            XMLComponentHandler.writeWallProjectionSampleToXML(sample);
                            Point3D p = new Point3D(vecs[2].X, vecs[2].Y, vecs[2].Z);
                            XMLComponentHandler.writeWallProjectionAndPositionSampleToXML(new WallProjectionAndPositionSample(sample, p));
                            XMLComponentHandler.writeSampleToXML(vecs, sample.sampleDeviceName);
                            dev.Add(d);
                            tempUser.lastChosenDeviceID = d.Id;
                            tempUser.lastClassDevSample = sample;
                            tempUser.deviceIDChecked = false;
                            return dev;
                        }
                    }
                }
            }
            return dev;
        }

        /// <summary>
        /// Adds a new coordinates and radius for a specified device by reading the right wrist position of the user 
        /// who wants to add them
        /// <param name="devId">the device to which the coordinate and radius should be added</param>
        /// <param name="wlanAdr">The wlan adress of the user who wants to add the coordinates and radius</param>
        /// <param name="radius">The radius specified by the user</param>
        /// <returns>A response string if the add process was successful</returns>
        /// </summary>
        private String AddDeviceCoord(String devId, String wlanAdr, String radius)
        {
            String ret = "keine Koordinaten hinzugefügt";

            double isDouble;
            if (!double.TryParse(radius, out isDouble) || String.IsNullOrEmpty(radius)) return ret += ",\nRadius fehlt oder hat falsches Format";


            if (Tracker.Bodies.Count != 0)
            {
                Vector3D rightWrist = Transformer.transformJointCoords(Tracker.getMedianFilteredCoordinates(Data.GetUserByIp(wlanAdr).SkeletonId))[3];
                Ball coord = new Ball(rightWrist, float.Parse(radius));
                Data.getDeviceByID(devId).Form.Add(coord);
                ret = XMLComponentHandler.addDeviceCoordToXML(devId, radius, coord);
            }

            return ret;
        }

        /// <summary>
        ///     Adds a new device to the device list and updates the deviceConfiguration part of the config.xml.
        ///     <param name="parameter">
        ///         Parameter of the device which should be added.
        ///         Parameter: Type, Name, Id, Form, Address
        ///     </param>
        ///     <returns>returns a response string what result the process had</returns>
        /// </summary>
        public String AddDevice(String[] parameter)
        {
            String retStr = "";

            int count = 1;
            for (int i = 0; i < Data.Devices.Count; i++)
            {
                String[] devId = Data.Devices[i].Id.Split('_');
                if (devId[0] == parameter[0])
                    count++;
            }
            string idparams = parameter[0] + "_" + count;

            
            XMLComponentHandler.addDeviceToXML(parameter, count);

            Type typeObject = Type.GetType("IGS.Server.Devices." + parameter[0]);
            if (typeObject != null)
            {
                object instance = Activator.CreateInstance(typeObject, parameter[1], idparams, new List<Ball>(),
                                                           parameter[2], parameter[3]);
                Data.Devices.Add((Device)instance);
                retStr = "Device added to deviceConfiguration.xml and devices list";

                Console.WriteLine(retStr);
                return retStr;
            }

            retStr = "Device added to deviceConfiguration but not to devices list";

            return retStr;
        }


        /// <summary>
        /// this method intiializes the representation of the kinect camera used for positioning and 
        /// visualization by reading the information out of the config.xml
        /// </summary>
        public void createIGSKinect()
        {
            float ballRad = 0.4f;

            String[] kinParamets = XMLComponentHandler.readKinectComponents();
            Vector3D kinectCenter = new Vector3D(double.Parse(kinParamets[0]), double.Parse(kinParamets[1]), double.Parse(kinParamets[2]));
            Ball kinectBall = new Ball(kinectCenter, ballRad);
            double roomOrientation = double.Parse(kinParamets[4]);
            double tiltingDegree = double.Parse(kinParamets[3]);


            IGSKinect = new devKinect("devKinect", kinectBall, tiltingDegree, roomOrientation);
        }

        public String collectSample(String wlan, String devID)
        {
            User tmpUser = Data.GetUserByIp(wlan);

            Device dev = Data.GetDeviceByName(devID);
            if (dev != null)
            {
                if (Tracker.Bodies.Count == 0)
                {
                    return "No bodys found by kinect";
                }
                Vector3D[] vectors = Transformer.transformJointCoords(Tracker.getMedianFilteredCoordinates(tmpUser.SkeletonId));
                //Vector3D[] vectors = Transformer.transformJointCoords(Tracker.GetCoordinates(tmpUser.SkeletonId));
                WallProjectionSample sample = classification.collector.calculateSample(vectors, dev.Name);
                if(sample.sampleDeviceName.Equals("nullSample") == false)
                {
                    classification.knnClassifier.pendingSamples.Add(sample);
                    Body body = Tracker.GetBodyById(tmpUser.SkeletonId);
                    writeUserJointsToXmlFile(tmpUser, dev,body);
                    XMLComponentHandler.writeUserJointsPerSelectClick(body);
                    XMLComponentHandler.writeWallProjectionSampleToXML(sample);
                    Point3D p = new Point3D(vectors[2].X, vectors[2].Y, vectors[2].Z);
                    XMLComponentHandler.writeWallProjectionAndPositionSampleToXML(new WallProjectionAndPositionSample(sample, p));
                    XMLComponentHandler.writeSampleToXML(vectors, sample.sampleDeviceName);
                    return "sample added";
                }
                else
                {
                    return "direction didn't hit a wall";
                }
            }
            return "Sample not added, deviceID not found";
        }

        /// <summary>
        ///     Creates or updates Log file with current user raw skeleton data.\n
        ///     <param name="user">current user</param>
        ///     <param name="device">current device</param>
        /// </summary>
        private void writeUserJointsToXmlFile(User user, Device device, Body body)
        {
            if (body == null)
            {
                Console.Out.WriteLine("No Body found, cannot write to xml");
                return;
            }
            String path = AppDomain.CurrentDomain.BaseDirectory + "\\BA_REICHE_LogFile.xml";

            //add device to configuration XML
            XmlDocument docConfig = new XmlDocument();

            if (File.Exists(path))
            {
                docConfig.Load(path);
            }
            else
            {
                docConfig.LoadXml("<data>" +
                                    "</data>");
            }


            XmlNode rootNode = docConfig.SelectSingleNode("/data");

            //try to find existing device
            XmlNode deviceNode = docConfig.SelectSingleNode("/data/device[@id='" + device.Id + "']");

            if (deviceNode == null)
            {
                //Create Device node
                XmlElement xmlDevice = docConfig.CreateElement("device");
                xmlDevice.SetAttribute("id", device.Id);
                xmlDevice.SetAttribute("name", device.Name);
                rootNode.AppendChild(xmlDevice);

                deviceNode = xmlDevice;
            }

            
            


            XmlElement xmlSkeleton = docConfig.CreateElement("skeleton");
            xmlSkeleton.SetAttribute("time", DateTime.Now.ToString("HH:mm:ss"));
            xmlSkeleton.SetAttribute("date", DateTime.Now.ToShortDateString());

            foreach (JointType jointType in Enum.GetValues(typeof(JointType)))
            {
                XmlElement xmlJoint = docConfig.CreateElement("joint");
                xmlJoint.SetAttribute("type", jointType.ToString());

                xmlJoint.SetAttribute("X", body.Joints[jointType].Position.X.ToString());
                xmlJoint.SetAttribute("Y", body.Joints[jointType].Position.Y.ToString());
                xmlJoint.SetAttribute("Z", body.Joints[jointType].Position.Z.ToString());
                xmlSkeleton.AppendChild(xmlJoint);

            }

            deviceNode.AppendChild(xmlSkeleton);


            docConfig.Save(path);

            
        }

        private void writeUserJointsPerSelectClick(User u, Body b)
        {
            if (b == null)
            {
                Console.Out.WriteLine("No Body found, cannot write to xml");
                return;
            }
            String path = AppDomain.CurrentDomain.BaseDirectory + "\\BA_REICHE_LogFilePerSelect.xml";

            //add device to configuration XML
            XmlDocument docConfig = new XmlDocument();

         
                docConfig.Load(path);
            
           

            XmlNode rootNode = docConfig.SelectSingleNode("/data");

            int select = int.Parse(rootNode.Attributes[0].InnerText);

            

            XmlElement xmlSelect = docConfig.CreateElement("select");
            XmlElement xmlSkeleton = docConfig.CreateElement("skeleton");
            xmlSelect.SetAttribute("time", DateTime.Now.ToString("HH:mm:ss"));
            xmlSelect.SetAttribute("date", DateTime.Now.ToShortDateString());
            xmlSkeleton.SetAttribute("skelID", b.TrackingId.ToString());

            foreach (JointType jointType in Enum.GetValues(typeof(JointType)))
            {
                XmlElement xmlJoint = docConfig.CreateElement("joint");
                xmlJoint.SetAttribute("type", jointType.ToString());

                xmlJoint.SetAttribute("X", b.Joints[jointType].Position.X.ToString());
                xmlJoint.SetAttribute("Y", b.Joints[jointType].Position.Y.ToString());
                xmlJoint.SetAttribute("Z", b.Joints[jointType].Position.Z.ToString());
                xmlSkeleton.AppendChild(xmlJoint);

            }
            xmlSelect.AppendChild(xmlSkeleton);
            rootNode.AppendChild(xmlSelect);
            rootNode.Attributes[0].InnerText = (select++).ToString();

            docConfig.Save(path);

        }

        


        public String getControlPagePathHttp(String id)
        {
            String controlPath = "";

            Type t = Data.getDeviceByID(id).GetType();

            controlPath = "http://" + Server.LocalIP + ":8080" + "/"+ t.Name + "/" +"index.html?dev=" + id;

            return controlPath;
        }

        public void executeOnlineLearning(String devId,  String wLanAdr)
        {
            User tmpUser = Data.GetUserByIp(wLanAdr);

            if (tmpUser == null || tmpUser.lastChosenDeviceID == null)
                return;

            if (devId == tmpUser.lastChosenDeviceID)
            {
                if (tmpUser.deviceIDChecked == false && tmpUser.lastClassDevSample != null)
                {
                    classification.onlineLearn(tmpUser);
                   
                    XMLComponentHandler.writeLogEntry("Executed OnlineLearning: Result: Device was classified correctly");
                    return;
                }
                return;
            }
            onlineNoSucces(devId, wLanAdr);
            
        }

        public void onlineNoSucces(String devId, String wLanAdr)
        {
            
            User tmpUser = Data.GetUserByIp(wLanAdr);

            
            if (tmpUser != null && tmpUser.deviceIDChecked == false && tmpUser.lastClassDevSample != null)
            {
                
                Device userDev = Data.getDeviceByID(tmpUser.lastChosenDeviceID);
                if (devId != userDev.Id)
                {
                    classification.deviceClassificationErrorCount++;
                    XMLComponentHandler.deleteLastUserSkeletonFromLogXML(userDev);
                    XMLComponentHandler.deleteLastSampleFromSampleLogs(userDev);
                    XMLComponentHandler.deleteLastUserSkeletonSelected();
                    tmpUser.deviceIDChecked = true;
                    tmpUser.lastClassDevSample = null;
                    tmpUser.lastChosenDeviceID = "";


                    Console.WriteLine("Wrong Device!");
                    XMLComponentHandler.writeLogEntry("Executed OnlineLearning: Result: Device was classified wrong");
                    learnedOnline = false;
                    return;
                }
            }

        }

        


       
        

     

    }
}