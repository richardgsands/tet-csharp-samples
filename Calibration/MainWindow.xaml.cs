/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */
using System;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using EyeTribe.Controls.Calibration;
//using EyeTribe.Controls.Cursor;
using EyeTribe.Controls.TrackBox;
using EyeTribe.ClientSdk.Data;
using Calibration.Cursor;
using System.Windows.Interop;
using EyeTribe.ClientSdk;
using MessageBox = System.Windows.MessageBox;

namespace Calibration
{
    public partial class MainWindow : IConnectionStateListener
    {
        private Screen activeScreen = Screen.PrimaryScreen;
        private CursorControl cursorControl;

        private bool isCalibrated;

        public MainWindow()
        {
            InitializeComponent();
            this.ContentRendered += (sender, args) => InitClient();
            this.KeyDown += MainWindow_KeyDown;
        }

        private void InitClient()
        {
            // Activate/connect client
            GazeManager.Instance.Activate(GazeManager.ApiVersion.VERSION_1_0, GazeManager.ClientMode.Push);

            // Listen for changes in connection to server
            GazeManager.Instance.AddConnectionStateListener(this);

            // Fetch current status
            OnConnectionStateChanged(GazeManager.Instance.IsActivated);

            // Add a fresh instance of the trackbox in case we reinitialize the client connection.
            TrackingStatusGrid.Children.Clear();
            TrackingStatusGrid.Children.Add(new TrackBoxStatus());

            UpdateState();
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e == null)
                return;

            switch (e.Key)
            {
                // Start calibration on hitting "C"
                case Key.C:
                    ButtonCalibrateClicked(this, null);
                    break;

                // Toggle mouse redirect with "M"
                case Key.M:
                    ButtonMouseClicked(this, null);
                    break;

                // Turn cursor control off on hitting Escape
                case Key.Escape:
                    if (cursorControl != null)
                        cursorControl.Enabled = false;

                    UpdateState();
                    break;
            }
        }

        public void OnConnectionStateChanged(bool IsActivated)
        {
            // The connection state listener detects when the connection to the EyeTribe server changes
            if (btnCalibrate.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => OnConnectionStateChanged(IsActivated)));
                return;
            }

            if (!IsActivated)
                GazeManager.Instance.Deactivate();

            UpdateState();
        }

        private void ButtonCalibrateClicked(object sender, RoutedEventArgs e)
        {
            // Check connectivitiy status
            if (GazeManager.Instance.IsActivated == false)
                InitClient();

            // API needs to be active to start calibrating
            if (GazeManager.Instance.IsActivated)
                Calibrate();
            else
                UpdateState(); // show reconnect
        }

        private void ButtonMouseClicked(object sender, RoutedEventArgs e)
        {
            if (GazeManager.Instance.IsCalibrated == false)
                return;

            if (cursorControl == null)
                cursorControl = new CursorControl(activeScreen, true, true, 100); // Lazy initialization
            else
                cursorControl.Enabled = !cursorControl.Enabled; // Toggle on/off

            UpdateState();
        }

        private void Calibrate()
        {
            // Update screen to calibrate where the window currently is
            activeScreen = Screen.FromHandle(new WindowInteropHelper(this).Handle);

            // Initialize and start the calibration
            CalibrationRunner calRunner = new CalibrationRunner(activeScreen, activeScreen.Bounds.Size, 9);
            calRunner.OnResult += calRunner_OnResult;
            calRunner.Start();
        }

        private void calRunner_OnResult(object sender, CalibrationRunnerEventArgs e)
        {
            // Invoke on UI thread since we are accessing UI elements
            if (RatingText.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => calRunner_OnResult(sender, e)));
                return;
            }

            // Show calibration results rating
            if (e.Result == CalibrationRunnerResult.Success)
            {
                isCalibrated = true;
                UpdateState();
            }
            else
                MessageBox.Show(this, "Calibration failed, please try again");
        }

        private void UpdateState()
        {
            // No connection
            if (GazeManager.Instance.IsActivated == false)
            {
                btnCalibrate.Content = "Connect";
                btnMouse.Content = "";
                RatingText.Text = "";
                return;
            }

            if (GazeManager.Instance.IsCalibrated == false)
            {
                btnCalibrate.Content = "Calibrate";
            }
            else
            {
                btnCalibrate.Content = "Recalibrate";

                // Set mouse-button label
                btnMouse.Content = "Mouse control On";

                if (cursorControl != null && cursorControl.Enabled)
                    btnMouse.Content = "Mouse control Off";

                if (GazeManager.Instance.LastCalibrationResult != null)
                    RatingText.Text = RatingFunction(GazeManager.Instance.LastCalibrationResult);
            }
        }

        private string RatingFunction(CalibrationResult result)
        {
            if (result == null)
                return "";

            double accuracy = result.AverageErrorDegree;

            if (accuracy < 0.5)
                return "Calibration Quality: PERFECT";

            if (accuracy < 0.7)
                return "Calibration Quality: GOOD";

            if (accuracy < 1)
                return "Calibration Quality: MODERATE";

            if (accuracy < 1.5)
                return "Calibration Quality: POOR";

            return "Calibration Quality: REDO";
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            GazeManager.Instance.Deactivate();
        }
    }
}

namespace Calibration.Cursor
{
    public class CursorControl : IGazeListener
    {
        #region Get/Set

        public bool Enabled { get; set; }
        public bool Smooth { get; set; }
        public int MovePointerThreshold { get; set; }     // change in pointer position needed (in pixels) to trigger mouse move
        public Screen ActiveScreen { get; set; }

        #endregion

        #region Constuctor

        public CursorControl()
            : this(Screen.PrimaryScreen, false, false)
        { }

        public CursorControl(Screen screen, bool enabled, bool smooth, int movePointerThreshold=0)
        {
            GazeManager.Instance.AddGazeListener(this);
            ActiveScreen = screen;
            Enabled = enabled;
            Smooth = smooth;
            MovePointerThreshold = movePointerThreshold;
        }

        #endregion

        #region Public interface methods

        public void OnGazeUpdate(GazeData gazeData)
        {
            if (!Enabled) return;

            // start or stop tracking lost animation
            if ((gazeData.State & GazeData.STATE_TRACKING_GAZE) == 0 &&
                (gazeData.State & GazeData.STATE_TRACKING_PRESENCE) == 0) return;

            // tracking coordinates
            var x = ActiveScreen.Bounds.X;
            var y = ActiveScreen.Bounds.Y;
            var gX = Smooth ? gazeData.SmoothedCoordinates.X : gazeData.RawCoordinates.X;
            var gY = Smooth ? gazeData.SmoothedCoordinates.Y : gazeData.RawCoordinates.Y;

            if ( moveThresholdMet(gX, gY) )
            {
                // move cursor

                var screenX = (int)Math.Round(x + gX, 0);
                var screenY = (int)Math.Round(y + gY, 0);

                // return in case of 0,0 
                if (screenX == 0 && screenY == 0) return;

                NativeMethods.SetCursorPos(screenX, screenY);

            }

        }

        #endregion

        #region Private helper methods

        private bool init = false;
        private float last_gX;
        private float last_gY;

        private bool moveThresholdMet(float gX, float gY)
        {
            // jump out if last values have not been set yet
            if (!init)
            {
                updateLastValues(gX, gY);
                init = true;
                return true;
            }

            var norm = Math.Sqrt(Math.Pow(gX - last_gX, 2) + Math.Pow(gY - last_gY, 2));
            updateLastValues(gX, gY);

            return (norm >= MovePointerThreshold);
        }

        private void updateLastValues(float gX, float gY)
        {
            last_gX = gX;
            last_gY = gY;
        }

        #endregion

        public class NativeMethods
        {
            [System.Runtime.InteropServices.DllImportAttribute("user32.dll", EntryPoint = "SetCursorPos")]
            [return: System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.Bool)]

            public static extern bool SetCursorPos(int x, int y);
        }
    }

}