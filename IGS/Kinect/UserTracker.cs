﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;
using Microsoft.Kinect;
using System;

namespace IGS.Server.Kinect
{
    /// <summary>
    ///     This class represents the management of the kinect internal data and provides them for the igs.
    ///     Part of the design pattern: subject(KinectUserEvent)
    ///     @author Sven Ochs
    /// </summary>
    public class UserTracker
    {
        /// <summary>
        ///     Delegate for the KinectUserEvent. With it it can be checked, if the event occured.
        ///     <param name="sender">Object which triggered the KinectUserEvent</param>
        ///     <param name="args">The event with its information</param>
        /// </summary>
        public delegate void KinectUserHandler(object sender, KinectUserEventArgs args);

        private Body[] _bodiesLastFrame = new Body[0];



        /// <summary>
        ///     Constructor of a Usertracker.
        ///     <param name='filter'>
        ///         The gesture a user activates the gesture control with.
        ///     </param>
        ///     <param name='replace'>
        ///         The strategy which specifies which skeleton should be replaced if to many users want to use gesture control
        ///     </param>
        /// </summary>
        public UserTracker(GestureStrategy filter, ReplacementStrategy replace)
        {
            Filter = filter;
            Strategy = replace;
            Bodies = new List<TrackedSkeleton>();
        }

        /// <summary>
        /// the list with all tracked skeleton
        /// </summary>
        public List<TrackedSkeleton> Bodies { get; set; }

        /// <summary>
        ///     The replacement strategy of the usertracker.
        ///     With the "set"-method, the replacement strategy of the usertracker can be set.
        ///     With the "get"-method, the replacement strategy of the usertracker can be returned.
        ///     <returns>Returns the replacement strategy of the usertracker</returns>
        /// </summary>
        public ReplacementStrategy Strategy { get; set; }

        /// <summary>
        ///     The gesture to activate gesture control of the usertracker.
        ///     With the "set"-method the gesture to activate gesture control used by the usertracker can be set.
        ///     With the "get"-method the gesture to activate gesture control used by the usertracker can be returned.
        ///     <returns>Returns the replacement strategy of the usertracker</returns>
        /// </summary>
        public GestureStrategy Filter { get; set; }

        /// <summary>
        ///     The kinect sensor
        ///     with the "get"-method the kinect sensor of the usertracker can be returned.
        ///     <returns>Returns the kinect sensor of the usertracker</returns>
        /// </summary>
        public KinectSensor Sensor { get; private set; }

        private BodyFrameReader reader = null;


        /// <summary>
        ///     Part of the design pattern: observer(KinectUserEvent)
        /// </summary>
        public virtual event KinectUserHandler KinectEvents;


        /// <summary>
        ///     Initializes the kinectsensor and listener for the kinect events
        /// </summary>
        public void InitializeSensor()
        {

            //foreach (KinectSensor potentialSensor in KinectSensor.KinectSensors.Where(potentialSensor => potentialSensor.Status == KinectStatus.Connected))
            //{
            //    Sensor = KinectSensor.Default;
            //    bodiesLastFrame = new Body[Sensor.BodyFrameSource.BodyCount];
            //    reader = Sensor.BodyFrameSource.OpenReader();
            //    break;
            //}
            Sensor = KinectSensor.GetDefault();
            _bodiesLastFrame = new Body[6];
            this.reader = Sensor.BodyFrameSource.OpenReader();

            if (Sensor == null) return;
            // Anschalten des Skelettstreams, zum empfangen der Skelette
            /*
            Sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
            Sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
            */

            // Start den Sensor!
            try
            {
                Sensor.Open();
            }
            catch (IOException)
            {
                Sensor = null;
            }
            if (this.reader != null)
            {
                this.reader.FrameArrived += this.reader_FramesReady;
            }
        }


        /// <summary>
        ///     Is called when a event occurs.
        ///     Part fo the design pattern: observer(KinectUserEvent)
        ///     Takes place for the notify-method in the observer design pattern
        ///     <param name="sender">object which triggered the event</param>
        ///     <param name="args">
        ///         The event with the id of the skeleton and the information if its left the room
        ///         or just is at the borders of the viewfield of the kinect.
        ///     </param>
        /// </summary>
        private void OnUserLeft(object sender, KinectUserEventArgs args)
        {
            // Falls es einen Abonnenten gibt, wird ein Event ausgelöst
            if (KinectEvents != null)
            {
                KinectEvents(this, args);
            }
        }

        /// <summary>
        ///     Shuts the kinectsensor down
        /// </summary>
        public void ShutDown()
        {
            if (Sensor != null)
            {
                Sensor.Close();
            }
        }

        /// <summary>
        ///     This method provides the skeletonID of the skeleton perfoming the definded gesture or which should be reaktivated.
        ///     
        ///     Returns the skeletonID of the user
        ///     <param name="igsSkelId">SkeletonID stored in the IGS</param>
        ///     <returns>ID of the skeleton which was marked as tracked</returns>
        /// </summary>
        public int GetSkeletonId(int igsSkelId)
        {
          
            if (Bodies.Any(s => s.Id == igsSkelId))
            {
                return igsSkelId;
            }

            Bodies = Strategy.Replace(Bodies);

            HashSet<int> idsSeen = new HashSet<int>();

            foreach (TrackedSkeleton s in Bodies)
            {
                idsSeen.Add(s.Id);
            }

            Bodies = Filter.Filter(_bodiesLastFrame, Bodies, igsSkelId, reader);

            ChooseSkeletons(_bodiesLastFrame);

            //herausfinden, welcher User hinzugekommen ist
            foreach (TrackedSkeleton s in Bodies.Where(s => !idsSeen.Contains(s.Id)))
            {
                
                return s.Id;
            }
           
            return -1;
        }


        /// <summary>
        ///     Tells the kinect which skeletons should be marked as tracked.
        /// </summary>
        private void ChooseSkeletons(IEnumerable<Body> skeletons)
        {
            if (Bodies.Count <= 0) return;
            int? id1 = null;
            int? id2 = null;

            foreach (TrackedSkeleton sTracked in Bodies)
            {
                if (!id1.HasValue)
                {
                    id1 = sTracked.Id;
                    continue;
                }
                id2 = sTracked.Id;
                break;
            }
            // Wähle die passende Anzahl für den Befehl ChooseSkeletons

            //if (!id1.HasValue)
            //{
            //    Sensor.SkeletonStream.ChooseSkeletons();
            //    return;
            //}
            //if (!id2.HasValue)
            //{
            //    Sensor.SkeletonStream.ChooseSkeletons(id1.Value);
            //    return;
            //}
            //Sensor.SkeletonStream.ChooseSkeletons(id1.Value, id2.Value);

        }

        /// <summary>
        ///     Interface to the IGS where the koordinates of ellbow/wrist of both arms regarding the kinect coordinate system of the kinect will be requested.
        ///     At postion 0 of the array is the vector of the left elbow.
        ///     At postion 1 of the array is the vector of the left wrist.
        ///     At postion 2 of the array is the vector of the right elbow.
        ///     At postion 3 of the array is the vector of the right wrist.
        ///     <param name="id">ID of the skeleton the coordinates are requested</param>
        ///     <returns>coordinates of the elbow/wrist as 3D-vector-array</returns>
        /// </summary>
        public Vector3D[] GetCoordinates(int id)
        {
            foreach (TrackedSkeleton sTracked in Bodies.Where(sTracked => sTracked.Id == id))
            {
                sTracked.Actions = sTracked.Actions + 1;
                foreach (Body s in _bodiesLastFrame)
                {
                    if ((int)s.TrackingId != id) continue;
                    Vector3D[] result = new Vector3D[4];
                    result[0] = new Vector3D(s.Joints[JointType.ElbowLeft].Position.X,
                                             s.Joints[JointType.ElbowLeft].Position.Y,
                                             s.Joints[JointType.ElbowLeft].Position.Z);
                    result[1] = new Vector3D(s.Joints[JointType.WristLeft].Position.X,
                                             s.Joints[JointType.WristLeft].Position.Y,
                                             s.Joints[JointType.WristLeft].Position.Z);
                    result[2] = new Vector3D(s.Joints[JointType.ElbowRight].Position.X,
                                             s.Joints[JointType.ElbowRight].Position.Y,
                                             s.Joints[JointType.ElbowRight].Position.Z);
                    result[3] = new Vector3D(s.Joints[JointType.WristRight].Position.X,
                                             s.Joints[JointType.WristRight].Position.Y,
                                             s.Joints[JointType.WristRight].Position.Z);
                    return result;
                }
            }
            return null;
        }


        /// <summary>
        /// Catches the AllFrameReady-Event and saves the skeletons of the actual frame before they are available to processing.
        /// Addtionally it checks, if a skeleton completely left the room.
        /// </summary>
        /// <param name="sender">sender of the AllFrameReady event</param>
        /// <param name="e">AllFrameReadyEvent with associated data.</param>
        public void reader_FramesReady(object sender, BodyFrameArrivedEventArgs e)
        {

            if (e == null)
                return;

            Body[] bodies = new Body[0];
            BodyFrameReference frameReference = e.FrameReference;

            using (BodyFrame bodyFrame = frameReference.AcquireFrame())
                if (bodyFrame != null)
                {
                    bodies = new Body[bodyFrame.BodyCount];
                    bodyFrame.GetAndRefreshBodyData(bodies);
                }
                else { return;  }




            HashSet<int> idsSeen = new HashSet<int>();



            foreach (Body s in bodies)
            {
                if (s.TrackingId != 0) idsSeen.Add((int)s.TrackingId);
            }


            bool bodiesLastFrameNotNull = false;

            foreach (Body s in _bodiesLastFrame)
            {
                if (s == null && bodiesLastFrameNotNull == true)
                {
                    bodiesLastFrameNotNull = false;
                    break;
                }
                if (s != null && bodiesLastFrameNotNull == false)
                {
                    bodiesLastFrameNotNull = true;
                }
            }

            if (bodiesLastFrameNotNull)
            {
                //checks if a skeleton doesnt exist anymore.
                foreach (Body s in _bodiesLastFrame.Where(s => s != null && !idsSeen.Contains((int)s.TrackingId) && s.TrackingId != 0))
                {
                    OnUserLeft(this, new KinectUserEventArgs((int)s.TrackingId));
                    for (int i = 0; i < Bodies.Count; i++)
                    {
                        if (Bodies[i].Id == (int)s.TrackingId)
                            Bodies.Remove(Bodies[i]);
                    }
                }
            }
            _bodiesLastFrame = bodies;


        }
    }
}