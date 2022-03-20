using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.KinectBowling
{
    class KinectBowlingController : IMachineControl
    {
        /// <summary>
        /// Holds the pixel data from the camera in a long-lived array that is re-written
        /// </summary>
        byte[] _colorCameraPixelData;

        // KinectSensor object. Represents the kinect device
        public KinectSensor KinectConnection { set; get; }

        /// <summary>
        /// Bitmap object with the current camera iamge with any ROI overlay
        /// </summary>
        public Bitmap KinectBitmap { set; get; }

        /// <summary>
        /// Pixelwise modifyable version of the kinect bitmap image
        /// </summary>
        public  DirectBitmap KinectDirectBitmap { set; get; }



        private BowlingConfig _bowlingConfig;


        static Size _pinRoiSize = new Size(6, 6);

        /// <summary>
        /// Used to draw debug information on-screen when a pin is reset back to its initial value. e.g. a pin was knocked over and then stood back up
        /// </summary>
        static Brush _pinScoreResetColor = new SolidBrush(Color.FromArgb(192, Color.Yellow));

        /// <summary>
        /// Used to draw debug information on-screen when a pin is in its initial position
        /// </summary>
        static Brush _pinSetColor = new SolidBrush(Color.FromArgb(192, Color.Green));

        /// <summary>
        /// Used to draw debug information on-screen when a pin was knocked over
        /// </summary>
        static Brush _pinScoreColor = new SolidBrush(Color.FromArgb(192, Color.Red));

        /// <summary>
        /// Width of the camera image
        /// </summary>
        private int KinectColorCameraImageWidth
        {
            get
            {
                if (KinectConnection != null && IsConnected)
                    return KinectConnection.ColorStream.FrameWidth;
                return 640; // fall back to probably-correct response
            }
        }

        /// <summary>
        /// Height of the camera image
        /// </summary>
        private int KinectColorCameraImageHieght
        {
            get
            {
                if (KinectConnection != null && IsConnected)
                    return KinectConnection.ColorStream.FrameHeight;

                return 480; // fall back to probably-correct response
            }
        }

        /// <summary>
        /// Minimum distance kinect can read
        /// </summary>
        internal int _minFrameDepth = 800;

        /// <summary>
        /// Maximum distance kinect can read
        /// </summary>
        int _maxFrameDepth = 4000;

        /// <summary>
        /// Contains the depth map from the latest camera refresh
        /// </summary>
        public DepthImagePixel[] DepthData { set; get; }
        
        /// <summary>
        /// Point we began drawing the ROI
        /// </summary>
        public Point DebugROIRectStart { set; get; } = Point.Empty;

        /// <summary>
        /// Point we stopped drawing the ROI
        /// </summary>
        public Point DebugROIRectStop { set; get; } = Point.Empty;
        
        /// <summary>
        /// Are we currently drawing the ROI
        /// </summary>
        public bool DebugIsDrawingROIRect { set; get; }

        /// <summary>
        /// Contains all depth pixels inside the drawn ROI
        /// </summary>
        public DepthImagePixel[] DebugROI { set; get; } = null;


        public KinectBowlingController(BowlingConfig bc)
        {
            _bowlingConfig = bc;
        }

        public bool IsConnected => KinectConnection != null ? KinectConnection.IsRunning : false;

        public bool IsLit => throw new NotImplementedException();

        public bool Connect()
        {
            return InitializeKinect();
        }

        public void Disconnect()
        {
            StopKinectSensor();
        }

        // initializes the KinectSensor object
        public bool InitializeKinect()
        {
            try
            {
                if (KinectSensor.KinectSensors.Count == 0)
                    return false;
                // tries to obtain the default KinectSensor instance.
                // There could be multiple Kinect devices connected at any one instance, 
                // so GetDefault() tries to obtain the default device.
                KinectConnection = KinectSensor.KinectSensors[0];

                // If we are unable to obtain a KinectSensor instance, exit with false.
                if (KinectConnection == null)
                    return false;
                
                if (!StartKinectSensor()) // Starts the KinectSensor
                    return false;

                KinectBitmap = new Bitmap(KinectConnection.ColorStream.FrameWidth, KinectConnection.ColorStream.FrameHeight);
                KinectDirectBitmap = new DirectBitmap(KinectConnection.ColorStream.FrameWidth, KinectConnection.ColorStream.FrameHeight);

                // We obtain the ColorFrameReader object from the KinectSensor
                // Create a Bitmap object with width and height that matches those
                // of the Kinect ColorFrameReader image.

                KinectConnection.ColorStream.Enable();
                KinectConnection.ColorFrameReady += Kinect_sensor_ColorFrameReady;

                _colorCameraPixelData = new byte[KinectConnection.ColorStream.FramePixelDataLength];



                KinectConnection.DepthStream.Enable();
                KinectConnection.DepthFrameReady += Kinect_sensor_DepthFrameReady;

                DepthData = new DepthImagePixel[KinectConnection.DepthStream.FramePixelDataLength];

            }
            catch (Exception ex)
            {
                throw ex;
            }

            return true;
        }

        private void Kinect_sensor_DepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (var frame = e.OpenDepthImageFrame())
            {

                if (frame == null)
                    return;

                try
                {
                    lock (DepthData)
                        frame.CopyDepthImagePixelDataTo(DepthData);


                    BowlingHelpers.UpdatePinRois(_bowlingConfig, DepthData, frame.Width, _minFrameDepth, _maxFrameDepth);
                }
                catch (Exception ex)
                {
                    throw ex;
                    //return false;
                }
            }
            
        }

        private void Kinect_sensor_ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            using (var frame = e.OpenColorImageFrame())
            {

                // Be careful, the returned ColorFrame object could be NULL.
                if (frame == null)
                    return;

                // Create an array of bytes that will store the pixels 
                // from the ColorFrameReader.
                lock (_colorCameraPixelData)
                    frame.CopyPixelDataTo(_colorCameraPixelData);
                

                try
                {
                    TransferPixelsToBitmapObject(KinectBitmap, _colorCameraPixelData);

                    DrawROIOverlay();

                    DrawPinROIOverlays(KinectDirectBitmap, _bowlingConfig);
                }
                catch (Exception ex)
                {
                    throw ex;
                }

            }
        }


        internal void DrawPinROIOverlays(DirectBitmap image, BowlingConfig BowlingConfig)
        {
            using (var graphics = Graphics.FromImage(image.Bitmap))
            {


                for (int k = 0; k < BowlingConfig.PinMatrix.Length; k++)
                {
                    //calc current cacheavg avg
                    var sum = 0.0;
                    if (BowlingConfig.PinMatrix[k].CachedAvgs.Count > 0)
                        sum = ReturnAverage(BowlingConfig.PinMatrix[k]);

                    var start = BowlingConfig.PinMatrix[k].Point;
                    var ROI = BowlingConfig.PinMatrix[k].ROI;
                    var avg = Math.Round(ROI.Average());


                    var height = BowlingConfig.PinMatrix[k].Height;
                    var width = BowlingConfig.PinMatrix[k].Width;


                    //e.Graphics.DrawString(outputVal, _myFont, _myBrush, start);
                    if (BowlingConfig.PinMatrix[k].Fallen)
                    {
                        // Color the pin yellow if it was scored on but the current reading is close to the initial reading (meaning the pin was reset)
                        if (BowlingConfig.PinMatrix[k].ResetIndicator)
                            graphics.FillRectangle(_pinScoreResetColor, new Rectangle(start, _pinRoiSize));
                        else
                            graphics.FillRectangle(_pinScoreColor, new Rectangle(start, _pinRoiSize));
                    }
                    else
                        graphics.FillRectangle(_pinSetColor, new Rectangle(start, _pinRoiSize));
                }
            }
        }

        internal static double ReturnAverage(BowlingPin bowlingPin)
        {
            var total = 0.0;
            var count = 0.0;
            foreach (var cachedAvg in bowlingPin.CachedAvgs)
            {
                if (cachedAvg > 800 && cachedAvg < 4000)
                {
                    total += cachedAvg;
                    count++;
                }
            }
            if (count == 0)
                return 0;
            return total / count;
        }

        /// <summary>
        /// Using the debug ROI functionality, draw the ROI on top of the color image
        /// </summary>
        private void DrawROIOverlay()
        {
            // If both points don't exist, don't update the ROI overlay
            if (DebugROIRectStop == Point.Empty || DebugROIRectStart == Point.Empty)
                return;

            // Check if our hand drawn ROI exists
            var width = Math.Abs(DebugROIRectStop.X - DebugROIRectStart.X);
            var height = Math.Abs(DebugROIRectStop.Y - DebugROIRectStart.Y);

            if (width > 1 && !DebugIsDrawingROIRect) // Also make sure we're not currently drawing the box
            {
                if (DebugROI == null)
                    DebugROI = new DepthImagePixel[width * height];

                // Get dimensions of the ROI
                width = Math.Abs(DebugROIRectStop.X - DebugROIRectStart.X);
                height = Math.Abs(DebugROIRectStop.Y - DebugROIRectStart.Y);


                int idx = 0;
                lock (DebugROI)
                {
                    // Ensure it's at least 1 pixel
                    if (width > 0 && height > 0)
                    {
                        // Check if the ROI array is large enough to hold the data, if it's not don't update anything
                        // Updating of the ROI array size is handled elsewhere
                        if (DebugROI.Length < width * height)
                            return;

                        // Iterate over the ROI dimensions
                        for (int j = DebugROIRectStart.Y; j < DebugROIRectStart.Y + height; j++)
                        {
                            for (int i = DebugROIRectStart.X; i < DebugROIRectStart.X + width; i++)
                            {
                                var pixelIdx = j * KinectColorCameraImageWidth + i; // Get array index for location
                                DebugROI[idx] = DepthData[pixelIdx]; // Grab the pixel from the depth map

                                idx++;
                            }
                        }
                    }
                }
                


                // Draw a greyscale depth map on top of the image
                for (int i = 0; i < DebugROI.Length; i++)
                {
                    // Check if the pixel is within the bounds of the kinect, if not, skip the pixel
                    if (DebugROI[i].Depth < _minFrameDepth || DebugROI[i].Depth > _maxFrameDepth)
                        continue;

                    // Convert the depth map value to a greyscale value
                    int f = DebugROI[i].Depth.Map(_minFrameDepth, _maxFrameDepth, 0, 255);

                    // Draw the value of the pixel on top of the color image
                    // TODO: Account for distortion of the camera projection
                    KinectDirectBitmap.SetPixel(DebugROIRectStart.X + (i % (width)), DebugROIRectStart.Y + (i / (width)), Color.FromArgb(f, f, f));
                }
            }
        }


        // Transfers a set of pixels to a target BitMap object.
        // The code for this method has been adapted from the example code given in
        // Bitmap.LockBits Method (Rectangle, ImageLockMode, PixelFormat)
        // https://msdn.microsoft.com/en-us/library/5ey6h79d(v=vs.110).aspx
        private void TransferPixelsToBitmapObject(Bitmap bmTarget, byte[] byPixelsForBitmap)
        {
            // Create a rectangle with width and height matching those of
            // the target bitmap object.
            Rectangle rectAreaOfInterest = new Rectangle(0, 0, bmTarget.Width, bmTarget.Height);

            // Lock the bits of the Bitmap object.
            BitmapData bmpData = bmTarget.LockBits(rectAreaOfInterest, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);

            IntPtr ptrFirstScanLineOfBitmap = bmpData.Scan0;

            int length = byPixelsForBitmap.Length;

            // Transfer all the data from byPixelsForBitmap to 
            // the pixel buffer for bmTarget.
            System.Runtime.InteropServices.Marshal.Copy(byPixelsForBitmap, 0, ptrFirstScanLineOfBitmap, length);
            // Unlock the bits.
            bmTarget.UnlockBits(bmpData);

            bmpData = null;
            using (var gr = Graphics.FromImage(KinectDirectBitmap.Bitmap))
                gr.DrawImage(KinectBitmap, 0, 0);
        }

        private bool StartKinectSensor()
        {
            // If the KinectSensor is not null AND is not open
            if (KinectConnection != null && (KinectConnection.IsRunning == false))
            {
                KinectConnection.Start(); // Opens the KinectSensor object 
                return true;
            }
            return false;
        }
        
        private void StopKinectSensor()
        {
            if ((KinectConnection != null) && (KinectConnection.IsRunning))
            {

                KinectConnection.Stop();
            }
        }

        public bool Init()
        {
            throw new NotImplementedException();
        }

        public void LightSwitch(bool on)
        {
            throw new NotImplementedException();
        }
    }
}
