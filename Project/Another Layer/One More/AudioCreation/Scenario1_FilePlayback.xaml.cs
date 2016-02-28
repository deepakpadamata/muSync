//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using SDKTemplate;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Render;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.Core;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;



// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace AudioCreation
{
    /// <summary>
    /// Using the AudioGraph API to playback from a file input.
    /// </summary>
    public sealed partial class Scenario1_FilePlayback : Page
    {
        private float[] dataInFloat = new float[500];
        private MainPage rootPage;

        private AudioGraph graph;
        private AudioFileInputNode fileInput;
        private AudioDeviceOutputNode deviceOutput;
        private AudioFrameOutputNode frameOutputNode;
        NetworkAdapter adapter = null;

        public Scenario1_FilePlayback()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = MainPage.Current;
            await CreateAudioGraph();

            // Make sure we're using the correct server address if an adapter was selected in scenario 1.
            object serverAddress;
            if (CoreApplication.Properties.TryGetValue("serverAddress", out serverAddress))
            {
                if (serverAddress is string)
                {
                    HostNameForConnect.Text = serverAddress as string;
                }
            }

            adapter = null;
            object networkAdapter;
            if (CoreApplication.Properties.TryGetValue("adapter", out networkAdapter))
            {
                adapter = (NetworkAdapter)networkAdapter;
            }
        }

        /// <summary>
        /// This is the click handler for the 'ConnectSocket' button.
        /// </summary>
        /// <param name="sender">Object for which the event was generated.</param>
        /// <param name="e">Event's parameters.</param>
        private async void ConnectSocket_Click(object sender, RoutedEventArgs e)
        {
            if (CoreApplication.Properties.ContainsKey("clientSocket"))
            {
                rootPage.NotifyUser(
                    "This step has already been executed. Please move to the next one.",
                    NotifyType.ErrorMessage);
                return;
            }

            if (String.IsNullOrEmpty(ServiceNameForConnect.Text))
            {
                rootPage.NotifyUser("Please provide a service name.", NotifyType.ErrorMessage);
                return;
            }

            // By default 'HostNameForConnect' is disabled and host name validation is not required. When enabling the
            // text box validating the host name is required since it was received from an untrusted source
            // (user input). The host name is validated by catching ArgumentExceptions thrown by the HostName
            // constructor for invalid input.
            HostName hostName;
            try
            {
                hostName = new HostName(HostNameForConnect.Text);
            }
            catch (ArgumentException)
            {
                rootPage.NotifyUser("Error: Invalid host name.", NotifyType.ErrorMessage);
                return;
            }

            StreamSocket socket = new StreamSocket();

            // If necessary, tweak the socket's control options before carrying out the connect operation.
            // Refer to the StreamSocketControl class' MSDN documentation for the full list of control options.
            socket.Control.KeepAlive = false;

            // Save the socket, so subsequent steps can use it.
            CoreApplication.Properties.Add("clientSocket", socket);
            try
            {
                if (adapter == null)
                {
                    rootPage.NotifyUser("Connecting to: " + HostNameForConnect.Text, NotifyType.StatusMessage);

                    // Connect to the server (by default, the listener we created in the previous step).
                    await socket.ConnectAsync(hostName, ServiceNameForConnect.Text);

                    rootPage.NotifyUser("Connected", NotifyType.StatusMessage);
                }
                else
                {
                    rootPage.NotifyUser(
                        "Connecting to: " + HostNameForConnect.Text +
                        " using network adapter " + adapter.NetworkAdapterId,
                        NotifyType.StatusMessage);

                    // Connect to the server (by default, the listener we created in the previous step)
                    // limiting traffic to the same adapter that the user specified in the previous step.
                    // This option will be overridden by interfaces with weak-host or forwarding modes enabled.
                    await socket.ConnectAsync(
                        hostName,
                        ServiceNameForConnect.Text,
                        SocketProtectionLevel.PlainSocket,
                        adapter);

                    rootPage.NotifyUser(
                        "Connected using network adapter " + adapter.NetworkAdapterId,
                        NotifyType.StatusMessage);
                }

                // Mark the socket as connected. Set the value to null, as we care only about the fact that the 
                // property is set.
                CoreApplication.Properties.Add("connected", null);
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                rootPage.NotifyUser("Connect failed with error: " + exception.Message, NotifyType.ErrorMessage);
            }
        }
        async private void SendHello(uint sizeOfArray)
        {
            if (!CoreApplication.Properties.ContainsKey("connected"))
            {
                rootPage.NotifyUser("Please run previous steps before doing this one.", NotifyType.ErrorMessage);
                return;
            }

            object outValue;
            StreamSocket socket;
            if (!CoreApplication.Properties.TryGetValue("clientSocket", out outValue))
            {
                rootPage.NotifyUser("Please run previous steps before doing this one.", NotifyType.ErrorMessage);
                return;
            }
            socket = (StreamSocket)outValue;

            // Create a DataWriter if we did not create one yet. Otherwise use one that is already cached.
            DataWriter writer;
            if (!CoreApplication.Properties.TryGetValue("clientDataWriter", out outValue))
            {
                writer = new DataWriter(socket.OutputStream);
                CoreApplication.Properties.Add("clientDataWriter", writer);
            }
            else
            {
                writer = (DataWriter)outValue;
            }

            writer.WriteUInt32(sizeOfArray);
            // Write first the length of the string as UINT32 value followed up by the string. 
            // Writing data to the writer will just store data in memory.
            DateTime now = DateTime.Now;
            //string stringToSend = now.ToString("yyyy-MM-ddTHH:mm:ss.fff");
            //writer.WriteUInt32(writer.MeasureString(stringToSend));
            //writer.WriteString(stringToSend);
            for (int i = 0; i < sizeOfArray; i++)
            {
                writer.WriteSingle(dataInFloat[i]);
            }
            
            // Write the locally buffered data to the network.
            try
            {
                await writer.StoreAsync();
                DateTime after = DateTime.Now;
                TimeSpan delay = after - now;
                Debug.WriteLine("RTT is " + delay.ToString() + " sent successfully.");
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error if fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                rootPage.NotifyUser("Send failed with error: " + exception.Message, NotifyType.ErrorMessage);
            }
        }
            

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Destroy the graph if the page is naviated away from
            if (graph != null)
            {
                graph.Dispose();
            }
        }

        private async void File_Click(object sender, RoutedEventArgs e)
        {
            // If another file is already loaded into the FileInput node
            if (fileInput != null)
            {
                // Release the file and dispose the contents of the node
                fileInput.Dispose();
                // Stop playback since a new file is being loaded. Also reset the button UI
                if (graphButton.Content.Equals("Stop Graph"))
                {
                    TogglePlay();
                }
            }

            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            filePicker.FileTypeFilter.Add(".mp3");
            filePicker.FileTypeFilter.Add(".wav");
            filePicker.FileTypeFilter.Add(".wma");
            filePicker.FileTypeFilter.Add(".m4a");
            filePicker.ViewMode = PickerViewMode.Thumbnail;
            StorageFile file = await filePicker.PickSingleFileAsync();

            // File can be null if cancel is hit in the file picker
            if(file == null)
            {
                return;
            }

            CreateAudioFileInputNodeResult fileInputResult = await graph.CreateFileInputNodeAsync(file);
            frameOutputNode = graph.CreateFrameOutputNode();
            graph.QuantumProcessed += AudioGraph_QuantumProcessed;
            if (AudioFileNodeCreationStatus.Success != fileInputResult.Status)
            {
                // Cannot read input file
                rootPage.NotifyUser(String.Format("Cannot read input file because {0}", fileInputResult.Status.ToString()), NotifyType.ErrorMessage);
                return;
            }

            fileInput = fileInputResult.FileInputNode;
            fileInput.AddOutgoingConnection(deviceOutput, 0.5);
            fileButton.Background = new SolidColorBrush(Colors.Green);

            // Trim the file: set the start time to 3 seconds from the beginning
            // fileInput.EndTime can be used to trim from the end of file
            fileInput.StartTime = TimeSpan.FromSeconds(0);

            // Enable buttons in UI to start graph, loop and change playback speed factor
            graphButton.IsEnabled = true;
            loopToggle.IsEnabled = true;
            //playSpeedSlider.IsEnabled = true;
        }

        private void Graph_Click(object sender, RoutedEventArgs e)
        {
            TogglePlay();
        }

        private void TogglePlay()
        { 
            //Toggle playback
            if (graphButton.Content.Equals("Start Graph"))
            {
                graph.Start();
                graphButton.Content = "Stop Graph";
                //audioPipe.Fill = new SolidColorBrush(Colors.Blue);
            }
            else
            {
                graph.Stop();
                graphButton.Content = "Start Graph";
                //audioPipe.Fill = new SolidColorBrush(Color.FromArgb(255, 49, 49, 49));
            }
        }

        //private void PlaySpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        //{
        //    if (fileInput != null)
        //    {
        //        //fileInput.PlaybackSpeedFactor = playSpeedSlider.Value;
        //    }
        //}

        private void LoopToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Set loop count to null for infinite looping
            // Set loop count to 0 to stop looping after current iteration
            // Set loop count to non-zero value for finite looping
            if (loopToggle.IsOn)
            {
                // If turning on looping, make sure the file hasn't finished playback yet
                if (fileInput.Position >= fileInput.Duration)
                {
                    // If finished playback, seek back to the start time we set
                    fileInput.Seek(fileInput.StartTime.Value);
                }
                fileInput.LoopCount = null; // infinite looping
            }
            else
            {
                fileInput.LoopCount = 0; // stop looping
            }
        }

        private async Task CreateAudioGraph()
        {
            // Create an AudioGraph with default settings
            AudioGraphSettings settings = new AudioGraphSettings(AudioRenderCategory.Media);
            CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);

            if (result.Status != AudioGraphCreationStatus.Success)
            {
                // Cannot create graph
                rootPage.NotifyUser(String.Format("AudioGraph Creation Error because {0}", result.Status.ToString()), NotifyType.ErrorMessage);
                return;
            }

            graph = result.Graph;
            
            // Create a device output node
            CreateAudioDeviceOutputNodeResult deviceOutputNodeResult = await graph.CreateDeviceOutputNodeAsync();

            if (deviceOutputNodeResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                // Cannot create device output node
                rootPage.NotifyUser(String.Format("Device Output unavailable because {0}", deviceOutputNodeResult.Status.ToString()), NotifyType.ErrorMessage);
                //speakerContainer.Background = new SolidColorBrush(Colors.Red);
                return;
            }

            deviceOutput = deviceOutputNodeResult.DeviceOutputNode;
            rootPage.NotifyUser("Device Output Node successfully created", NotifyType.StatusMessage);
            //speakerContainer.Background = new SolidColorBrush(Colors.Green);
        }
 

        private void AudioGraph_QuantumProcessed(AudioGraph sender, object args)
        {
            AudioFrame frame = frameOutputNode.GetFrame();
            ProcessFrameOutput(frame);

        }

        unsafe private void ProcessFrameOutput(AudioFrame frame)
        {
            using (AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (IMemoryBufferReference reference = buffer.CreateReference())
            {
                byte* dataInBytes;
                uint capacityInBytes;
                

                // Get the buffer from the AudioFrame
                ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacityInBytes);

                for (int i = 0; i < capacityInBytes; i++)
                {
                    dataInFloat[i] = *((float*)dataInBytes + i);
                }
                SendHello(capacityInBytes);
            }
        }

        private void SampleDescription_SelectionChanged(object sender, RoutedEventArgs e)
        {

        }
    }
}
