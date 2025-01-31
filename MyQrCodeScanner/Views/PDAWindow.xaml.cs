﻿using AForge.Video;
using AForge.Video.DirectShow;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WindowsInput;
using WindowsInput.Native;
using ZXing;

namespace MyQrCodeScanner
{
    public partial class PDAWindow : Window, INotifyPropertyChanged
    {
        #region Public properties

        private string inputmode;

        public string InputMode
        {
            get { return inputmode; }
            set
            {
                inputmode = value;
                this.OnPropertyChanged("InputMode");
                IniHelper.SetKeyValue("main", "inputmode", inputmode, IniHelper.inipath);
            }
        }

        private string audiopath;

        public string AudioPath
        {
            get { return audiopath; }
            set
            {
                audiopath = value;
                this.OnPropertyChanged("AudioPath");
                IniHelper.SetKeyValue("main", "audiopath", audiopath, IniHelper.inipath);
            }
        }

        private bool playaudio;

        public bool PlayAudio
        {
            get { return playaudio; }
            set
            {
                playaudio = value;
                this.OnPropertyChanged("PlayAudio");
                IniHelper.SetKeyValue("main", "playaudio", playaudio.ToString(), IniHelper.inipath);
            }
        }

        public ObservableCollection<FilterInfo> VideoDevices { get; set; }

        public FilterInfo CurrentDevice
        {
            get { return _currentDevice; }
            set { _currentDevice = value; this.OnPropertyChanged("CurrentDevice"); }
        }
        private FilterInfo _currentDevice;
        private System.Drawing.Bitmap img,imgbuffer;
        private System.Timers.Timer timer;

        #endregion

        #region Private fields

        private IVideoSource _videoSource;
        TaskbarIcon myTaskbarIcon;

        #endregion

        #region 构造函数
        public PDAWindow()
        {
            AudioPath = IniHelper.GetKeyValue("main", "audiopath", "", IniHelper.inipath);
            InputMode = IniHelper.GetKeyValue("main", "inputmode", "1", IniHelper.inipath);
            PlayAudio = Convert.ToBoolean(IniHelper.GetKeyValue("main", "playaudio", "false", IniHelper.inipath));
            GetVideoDevices();
            var lastdevice= IniHelper.GetKeyValue("main", "LastVideoDevice", "", IniHelper.inipath);
            foreach (FilterInfo d in VideoDevices)
                if (d.Name == lastdevice)
                    CurrentDevice = d;
            InitializeComponent();
            this.DataContext = this;
            this.Closing += MainWindow_Closing;
        }
        #endregion

        #region 主功能
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            //StopCamera();
            if (Application.Current.MainWindow == null)
            {
                StopCamera();
                return;
            }
            e.Cancel = true;
            this.Hide();
            myTaskbarIcon.ShowBalloonTip("程序将在后台运行", "若要退出：右击托盘区图标点击退出程序", BalloonIcon.Info);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AddTimer();
            myTaskbarIcon = (TaskbarIcon)FindResource("Taskbar");
            //_taskbar.DataContext = new NotifyIconViewModel();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            StartCamera();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }

        private void ButtonOpenAudio_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.CheckFileExists = true;
            dlg.Multiselect = false;
            dlg.Filter = "All Supported Files | *.wav; *.ogg; *.mp1; *.m1a; *.mp2; " +
                         "*.m2a;*.mpa;*.mus;*.mp3;*.mpg;*.mpeg;*.mp3pro;" +
                         "*.aif;*.aiff;*.bwf;*.wma;*.wmv;*.aac;*.adts;" +
                         "*.mp4;*.m4a;*.m4b;*.m4p;*.mod;*.mdz;*.mo3;*.s3m;" +
                         "*.s3z;*.xm;*.xmz;*.it;*.itz;*.umx;*.mtm;*.m4a;" +
                         "*.m4b;*.mp4;*.ac3;*.ape;*.mac;*.dff;*.dsf;" +
                         "*.flac;*.fla;*.oga;*.ogg;*.midi;*.mid;*.rmi;" +
                         "*.kar;*.opus;*.webm;*.mkv;*.mka";
            var res = dlg.ShowDialog();
            if(res == true)
            {
                AudioPath = dlg.FileName;
            }
        }
        #endregion

        #region Camera
        private void video_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try
            {
                BitmapImage bi;
                imgbuffer = img;
                img = (System.Drawing.Bitmap)eventArgs.Frame.Clone();
                //Console.WriteLine("f");
                bi = BitmapHelper.GetBitmapImage(img);
                
                bi.Freeze(); // avoid cross thread operations and prevents leaks
                Dispatcher.BeginInvoke(new ThreadStart(delegate { videoPlayer.Source = bi; }));
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error on _videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopCamera();
            }
        }


        private void GetVideoDevices()
        {
            VideoDevices = new ObservableCollection<FilterInfo>();
            foreach (FilterInfo filterInfo in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            {
                VideoDevices.Add(filterInfo);
            }
            if (VideoDevices.Any())
            {
                CurrentDevice = VideoDevices[0];
            }
            else
            {
                MessageBox.Show("No video sources found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartCamera()
        {
            hinttext.Visibility= Visibility.Collapsed; 
            if (CurrentDevice != null)
            {
                _videoSource = new VideoCaptureDevice(CurrentDevice.MonikerString);
                _videoSource.NewFrame += video_NewFrame;
                _videoSource.Start();
                IniHelper.SetKeyValue("main", "LastVideoDevice", CurrentDevice.Name, IniHelper.inipath);
            }
            ClearResult();
            timer.Start();
        }

        private void StopCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.NewFrame -= new NewFrameEventHandler(video_NewFrame);
            }
            img = null;
            timer.Stop();
        }

        #endregion

        #region 扫描

        private void AddTimer()
        {
            timer = new System.Timers.Timer();
            timer.Interval = 300;
            timer.Elapsed += new System.Timers.ElapsedEventHandler(timertick);
        }

        private void timertick(object sender, System.Timers.ElapsedEventArgs e)
        {
            PicDecode1();
        }

        private void PicDecode1()
        {
            timer.Stop();
            if (imgbuffer == null)
            {
                timer.Start(); return;
            }

            var res = MyScanner.ScanCode(imgbuffer);
            switch (res.status)
            {
                case result_status.error:
                    timer.Start();
                    break;
                case result_status.ok:
                    if (res.data[0].data != MyResult)
                    {
                        if (res.data[0].data != "" && MyResult == "")
                            DoPaste(res.data[0].data);
                        MyResult = res.data[0].data;
                    }
                    timer.Start();
                    break;
                case result_status.nocode:
                    MyResult = "";
                    timer.Start();
                    break;
            }
                
        }

        #endregion

        #region 扫描后
        private string MyResult;

        public void ClearResult()
        {
            MyResult = "";
        }

        private void DoPaste(string s)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (PlayAudio) 
                    PlayAudioFile();

                InputSimulator k = new InputSimulator();
                if (InputMode == "1")
                {
                    Clipboard.SetText(s);
                    Thread.Sleep(50);
                    k.Keyboard.KeyDown(VirtualKeyCode.CONTROL);
                    k.Keyboard.KeyPress(VirtualKeyCode.VK_V);
                    k.Keyboard.KeyUp(VirtualKeyCode.CONTROL);
                }
                else
                    k.Keyboard.TextEntry(s);
            });
        }

        private void PlayAudioFile()
        {
            try
            {
                player.LoadedBehavior = MediaState.Manual;
                player.Source = new Uri(AudioPath);
                player.Volume = 100;
                player.Play();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                MessageBox.Show(ex.ToString(), "播放音频时发生错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        #endregion

        #region INotifyPropertyChanged members

        public event PropertyChangedEventHandler PropertyChanged;


        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }

        #endregion
    }
}
