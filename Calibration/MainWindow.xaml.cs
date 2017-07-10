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
using System.Windows.Media.Animation;
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
                cursorControl = new CursorControl(activeScreen, true, true, 100, 100); // Lazy initialization
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
        public int MovePointerThresholdPos { get; set; }         // change in pointer position needed (in pixels) to trigger mouse move
        public int MovePointerThresholdTime { get; set; }     // time at new position required to trigger mouse move
        public Screen ActiveScreen { get; set; }

        private const int CURSOR_ANIMATION_DUR = 200;
        private const int CURSOR_ANIMATION_INVL = 33;       // 30 fps

        #endregion

        #region Constuctor

        public CursorControl()
            : this(Screen.PrimaryScreen, false, false)
        { }

        public CursorControl(Screen screen, bool enabled, bool smooth, int movePointerThresholdPos=0, int movePointerThresholdTime = 0)
        {
            GazeManager.Instance.AddGazeListener(this);
            ActiveScreen = screen;
            Enabled = enabled;
            Smooth = smooth;
            MovePointerThresholdPos = movePointerThresholdPos;
            MovePointerThresholdTime = movePointerThresholdTime;

            cursorAnimationTimer.Interval = CURSOR_ANIMATION_INVL;
            cursorAnimationTimer.Tick += new EventHandler(cursorAnimationTimerEventHandler);
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

            var screenX = (int)Math.Round(x + gX, 0);
            var screenY = (int)Math.Round(y + gY, 0);

            // return in case of 0,0 
            if (screenX == 0 && screenY == 0) return;

            handleNewScreenGazePosition(new System.Drawing.Point(screenX, screenY));

        }

        #endregion

        #region Private helper methods
        private System.Drawing.Point lastPosition;

        private System.Windows.Forms.Timer cursorAnimationTimer = new System.Windows.Forms.Timer();
        private System.Drawing.Point cursorAnimationTimerPositionGoal;
        private System.Drawing.Point cursorAnimationTimerPositionCurrent;
        private System.Drawing.Point cursorAnimationTimerPositionInterval;

        private void handleNewScreenGazePosition(System.Drawing.Point position)
        {
            lastPosition = System.Windows.Forms.Cursor.Position;
            var norm = Math.Sqrt(Math.Pow(position.X - lastPosition.X, 2) + Math.Pow(position.Y - lastPosition.Y, 2));

            if (norm >= MovePointerThresholdPos)
            {
                //System.Windows.Forms.Cursor.Position = position;
                setCursorPosAnimate(position, lastPosition);
            }
        }
    
        private void setCursorPosAnimate(System.Drawing.Point position, System.Drawing.Point lastPosition)
        {
            int cursorAnimationSteps = Convert.ToInt32(CURSOR_ANIMATION_DUR / CURSOR_ANIMATION_INVL);

            int cursorAnimationDeltaX = Convert.ToInt32((position.X - lastPosition.X) / cursorAnimationSteps);
            int cursorAnimationDeltaY = Convert.ToInt32((position.Y - lastPosition.Y) / cursorAnimationSteps);

            cursorAnimationTimer.Enabled = false;

            cursorAnimationTimerPositionCurrent = lastPosition;
            cursorAnimationTimerPositionGoal = position;
            cursorAnimationTimerPositionInterval = new System.Drawing.Point(cursorAnimationDeltaX, cursorAnimationDeltaY);

            cursorAnimationTimer.Enabled = true;
        }

        private void cursorAnimationTimerEventHandler(Object timerObject, EventArgs timerEventArgs)
        {
            cursorAnimationTimerPositionCurrent.Offset(cursorAnimationTimerPositionInterval);

            if (cursorAnimationTimerPositionCurrent.X > cursorAnimationTimerPositionGoal.X || cursorAnimationTimerPositionCurrent.Y > cursorAnimationTimerPositionGoal.Y)
            {
                cursorAnimationTimer.Enabled = false;
                System.Windows.Forms.Cursor.Position = cursorAnimationTimerPositionGoal;
                return;
            }

            System.Windows.Forms.Cursor.Position = cursorAnimationTimerPositionCurrent;
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