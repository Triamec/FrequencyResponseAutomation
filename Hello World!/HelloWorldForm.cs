using System;
using NIRange = NationalInstruments.UI.Range;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Triamec.FrequencyResponse;
using Triamec.FrequencyResponse.Configuration;
using Triamec.Tam.FrequencyResponse.NUnit;
using Triamec.Tam.Configuration;
using Triamec.Tam.FrequencyResponse;
using Triamec.Tam.Samples.Properties;
using Triamec.Tam.Subscriptions;
using Triamec.TriaLink;
using Triamec.TriaLink.Adapter;
using Triamec.TriaLink.Subscriptions;


// Rlid19 represents the register layout of drives of the current generation. A previous generation drive has layout 4.
using Axis = Triamec.Tam.Rlid19.Axis;
using Triamec.Diagnostics;

namespace Triamec.Tam.Samples {
    /// <summary>
    /// The main form of the TAM "Hello World!" application.
    /// </summary>
    internal partial class HelloWorldForm : Form {
        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="HelloWorldForm"/> class.
        /// </summary>
        public HelloWorldForm() {
            InitializeComponent();
        }
        #endregion Constructor

        #region Hello world code
        /// <summary>
        /// The configuration file for simulated mode.
        /// </summary>
        const string ConfigurationPath = "HelloWorld.TAMcfg";

        /// <summary>
        /// The name of the axis this demo works with.
        /// </summary>
        // CAUTION!
        // Selecting the wrong axis can have unintended consequences.
        const string AxisName = "Axis 1";

        /// <summary>
        /// The distance to move when pressing one of the move buttons.
        /// </summary>
        // CAUTION!
        // The unit of this constant depends on the PositionUnit parameter provided with the TAM configuration.
        // Additionally, the encoder must be correctly configured.
        // Consider any limit stops.
        const double Distance = 0.5 * Math.PI;

        /// <summary>
        /// Whether to use a (rather simplified) simulation of the axis.
        /// </summary>
        // CAUTION!
        // Ensure the above constants are properly configured before setting this to false.
        readonly bool _offline = false;

        TamTopology _topology;
        TamAxis _axis;

        float _velocityMaximum;
        string _unit;

        FrequencyResponseLogicCallback _callback;

        /// <summary>
        /// Prepares the TAM system.
        /// </summary>
        /// <exception cref="TamException">Startup failed.</exception>
        /// <exception cref="Triamec.Configuration.ConfigurationException">Failed to load the configuration.</exception>
        /// <remarks>
        /// 	<list type="bullet">
        /// 		<item><description>Creates a TAM topology,</description></item>
        /// 		<item><description>boots the Tria-Link,</description></item>
        /// 		<item><description>searches for a servo-drive,</description></item>
        /// 		<item><description>loads and applies a TAM configuration.</description></item>
        /// 	</list>
        /// </remarks>
        void Startup() {

            // Create the root object representing the topology of the TAM hardware.
            // We will dispose this object via components.
            _topology = new TamTopology();
            components.Add(_topology);

            TamSystem system;
            if (_offline) {
                using (var deserializer = new Deserializer()) {

                    // Load and add a simulated TAM system as defined in the .TAMcfg file.
                    deserializer.Load(ConfigurationPath);
                    var adapters = CreateSimulatedTriaLinkAdapters(deserializer.Configuration).First();
                    system = _topology.ConnectTo(adapters.Key, adapters.ToArray());

                    // Boot the Tria-Link so that it learns about connected stations.
                    system.Identify();
                }

                // Load a TAM configuration.
                // This API doesn't feature GUI. Refer to the Gear Up! example which uses an API exposing a GUI.
                _topology.Load(ConfigurationPath);
            } else {

                // Add the local TAM system on this PC to the topology.
                system = _topology.AddLocalSystem();

                // Boot the Tria-Link so that it learns about connected stations.
                system.Identify();

                // Don't load TAM configuration, assuming that the drive is already configured,
                // for example since parametrization is persisted in the drive.
            }

            // Find the axis with the configured name in the Tria-Link.
            // The AsDepthFirstLeaves extension method performs a tree search an returns all instances of type TamAxis.
            // "Leaves" means that the search doesn't continue within TamAxis nodes.
            _axis = system.AsDepthFirstLeaves<TamAxis>().FirstOrDefault(a => a.Name == AxisName);
            if (_axis == null) throw new TamException(Resources.NoAxisMessage);

            // Most drives get integrated into a real time control system. Accessing them via TAM API like we do here is considered
            // a secondary use case. Tell the axis that we're going to take control. Otherwise, the axis might reject our commands.
            // You should not do this, though, when this application is about to access the drive via the PCI interface.
            _axis.ControlSystemTreatment.Override(enabled: true);

            // Simulation always starts up with LinkNotReady error, which we acknowledge.
            if (_offline) _axis.Drive.ResetFault();

            // Get the register layout of the axis
            // and cast it to the RLID-specific register layout.
            var register = (Axis)_axis.Register;

            // Read and cache the original velocity maximum value,
            // which was applied from the configuration file.
            _velocityMaximum = register.Parameters.PathPlanner.VelocityMaximum.Read();

            // Cache the position unit.
            _unit = register.Parameters.PositionController.PositionUnit.Read().ToString();

            // Start displaying the position in regular intervals.
            _timer.Start();
        }

        /// <summary>
        /// Creates simulated Tria-Link adapters from a specified configuration.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>The newly created simulated Tria-Link adapters.</returns>
        static IEnumerable<IGrouping<Uri, ITriaLinkAdapter>> CreateSimulatedTriaLinkAdapters(
            TamTopologyConfiguration configuration) =>

            // This call must be in this extra method such that the Tam.Simulation library is only loaded
            // when simulating. This happens when this method is jitted because the SimulationFactory is the first
            // symbol during runtime originating from the Tam.Simulation library.
            SimulationFactory.FromConfiguration(configuration, null);

        /// <exception cref="TamException">Enabling failed.</exception>
        void EnableDrive() {

            // Set the drive operational, i.e. switch the power section on.
            _axis.Drive.SwitchOn();

            // Reset any axis error and enable the axis controller.
            _axis.Control(AxisControlCommands.ResetErrorAndEnable);
        }

        /// <exception cref="TamException">Disabling failed.</exception>
        void DisableDrive() {

            // Disable the axis controller.
            _axis.Control(AxisControlCommands.Disable);

            // Switch the power section off.
            _axis.Drive.SwitchOff();
        }

        /// <summary>
        /// Moves in the specified direction.
        /// </summary>
        /// <param name="sign">A positive or negative value indicating the direction of the motion.</param>
        /// <exception cref="TamException">Moving failed.</exception>
        void MoveAxis(int sign) =>

            // Move a distance with dedicated velocity.
            // If the axis is just moving, it is reprogrammed with this command.
            _axis.MoveRelative(Math.Sign(sign) * Distance, _velocityMaximum);

        void Measure() {

        }
        #endregion Hello world code

        #region GUI handler methods
        #region Form handler methods

        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);

            try {
                Startup();
                _driveGroupBox.Enabled = true;
            } catch (TamException ex) {
                MessageBox.Show(this, ex.FullMessage(), Resources.StartupErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, 0);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            base.OnFormClosed(e);
            if (_axis != null) {
                try {
                    DisableDrive();
                } catch (TamException ex) {
                    MessageBox.Show(this, ex.Message, Resources.StartupErrorCaption, MessageBoxButtons.OK,
                        MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, 0);
                }
            }
        }
        #endregion Form handler methods

        #region Button handler methods

        void OnEnableButtonClick(object sender, EventArgs e) {
            try {
                EnableDrive();
            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.EnablingErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }

            // Note: a more elaborated application would change button states depending on what's the drive reporting,
            // following the MVC concept.
            _moveNegativeButton.Enabled = true;
            _movePositiveButton.Enabled = true;
        }

        void OnDisableButtonClick(object sender, EventArgs e) {
            _moveNegativeButton.Enabled = false;
            _movePositiveButton.Enabled = false;
            try {
                DisableDrive();
            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.DisablingErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }
        }

        void OnMoveNegativeButtonClick(object sender, EventArgs e) {
            try {
                MoveAxis(-1);
            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.MoveErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }
        }

        void OnMovePositiveButtonClick(object sender, EventArgs e) {
            try {
                MoveAxis(1);
            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.MoveErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }
        }

        private void OnMeasureButtonClick(object sender, EventArgs e) {
            // Does not contain any asserts, but ensures the principal Frequency Response acquirement mechanism is tested

            const string negativeSign = "^";
            const string decimalSeparator = "°";
            const string listSeparator = "¨";

            #region Setup special culture
            var culture = new CultureInfo(CultureInfo.InvariantCulture.LCID, false);

            // make the current culture somewhat disturbed
            culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
            culture.NumberFormat.NegativeSign = negativeSign;
            culture.TextInfo.ListSeparator = listSeparator;

            Thread thread = Thread.CurrentThread;
            CultureInfo backupCulture = thread.CurrentCulture;
            thread.CurrentCulture = culture;
            #endregion Setup special culture

            //FrequencyResponseLogicCallback _callback = _callback = new FrequencyResponseLogicCallback(logic, signal, this, formatProvider, log); ;



            try {
                FrequencyResponseAxis axis = SetupFrequencyResponseAxis();

                var signal = new AutoResetEvent(initialState: false);

                Func<AutoResetEvent, IFrequencyResponseAxis, CultureInfo, IFrequencyResponseLogic> worker = StartFrequencyResponse;
                IAsyncResult asyncResult = worker.BeginInvoke(signal, axis, culture, null, null);
                try {
                    var wait = new TimeSpan(0, 0, 3, 0, 0);
                    if (!signal.WaitOne(wait, false)) {
                        //Assert.Fail(string.Format("The test duration exceeded {0} minutes", wait.TotalMinutes));
                    }
                } finally {
                    IFrequencyResponseLogic logic = worker.EndInvoke(asyncResult);
                    logic.GetFrequencyResponseResultCancel();
                    logic.Dispose();
                    axis.Tidy();
                    string signalsFile = _callback.SignalsFile;
                    string resultFile = _callback.ResultFile;
                    _callback.Dispose();

                }
            } finally {
                thread.CurrentCulture = backupCulture;

                //// save space in report dirs.
                //if (File.Exists(_callback.SignalsFile)) File.Delete(_callback.SignalsFile);
                //if (File.Exists(_callback.ResultFile)) File.Delete(_callback.ResultFile);
            }
        }

        //IFrequencyResponseLogic StartFrequencyResponse(AutoResetEvent signal, IFrequencyResponseAxis axis, CultureInfo formatProvider, ILog log) {
        //    IFrequencyResponseLogic logic = new FrequencyResponseLogic();
        //    var parameters = new FrequencyResponseParameters(3) {
        //        FrequencyRange = new NIRange(10, 10000),
        //        FrequencySteps = 10,
        //        Spacing = FrequencySpacing.Logarithmic,
        //        SettlingTime = TimeSpan.FromSeconds(0.2)
        //    };
        //    parameters.SetMeasuringPointMaximum(0, 10);
        //    parameters.SetMeasuringPointMaximum(1, 0.8);
        //    parameters.SetMeasuringPointMaximum(2, 100);

        //    _callback = new FrequencyResponseLogicCallback(logic, signal, this, formatProvider, log);

        //    logic.GetFrequencyResponseResultAsync(axis, parameters);
        //    return logic;
        //}

        FrequencyResponseAxis SetupFrequencyResponseAxis() =>

            // open loop
            new FrequencyResponseAxis(_axis) {
                MeasurementMethod = FrequencyResponseConfig.Read().MeasurementMethods[0]
            };
        #endregion SetupFrequencyResponseAxis

        #region FrequencyResponseLogic helpers
        /// <summary>
        /// Create the Frequency Response logic on another thread than the thread who will wait for completion
        /// </summary>
        /// <param name="signal">The signal where completion will be signaled.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <param name="log">The log.</param>
        /// <returns>
        /// The created resource that must be managed by the caller.
        /// </returns>
        IFrequencyResponseLogic StartFrequencyResponse(AutoResetEvent signal, IFrequencyResponseAxis axis, CultureInfo formatProvider) {
            IFrequencyResponseLogic logic = new FrequencyResponseLogic();
            var parameters = new FrequencyResponseParameters(3) {
                FrequencyRange = new NIRange(10, 10000),
                FrequencySteps = 10,
                Spacing = FrequencySpacing.Logarithmic,
                SettlingTime = TimeSpan.FromSeconds(0.2)
            };
            parameters.SetMeasuringPointMaximum(0, 10);
            parameters.SetMeasuringPointMaximum(1, 0.8);
            parameters.SetMeasuringPointMaximum(2, 100);

            _callback = new FrequencyResponseLogicCallback(logic, signal, formatProvider);

            logic.GetFrequencyResponseResultAsync(axis, parameters);
            return logic;
        }

        /// <summary>
        /// Gets the proportion between the frame counter period and the period of the packets acquired by the Frequency Response test.
        /// </summary>
        //ushort FrameCountToPacketResolution(FrequencyResponseAxis axis) {

        //    var samplingTime = axis.SamplingTime;
        //    var subscriptionSpeed = Enum.GetValues(typeof(SubscriptionSpeed))
        //                                .OfType<SubscriptionSpeed>()
        //                                .Single(speed => axis.GetSamplingTime(speed) == axis.SamplingTime);
        //    PublishSpeed publishSpeed = axis.ComputePublishSpeed(subscriptionSpeed);
        //    var frameCountResolution = axis.FrameCountResolution;
        //    return (ushort)(frameCountResolution * publishSpeed.SamplesPerPacket);
        //}

        /// <summary>
        /// Gets the maximum size of a measurement in the unit of <see cref="SignalParameters.FrameSize"/>.
        /// </summary>
        //ushort MaximalFrameSize(FrequencyResponseAxis axis) {

        //    // When the trigger is first activated, the published values must all belong to the frame.
        //    // We cannot just take 0 as level because then we would publish data from the time when the frame counter
        //    // was actually at level -1.
        //    // the last sample we get is at least one index above triggerLevel
        //    // with FrameCountToPacketResolution(axis) = 10, we have this possible sample ranges for the 1st packet:
        //    // [0..9]..[8..17]
        //    var triggerLevel = FrameCountToPacketResolution(axis) - 1;

        //    // This value could be one sample smaller because the drive produces values from 0..frameSize which is
        //    // actually one sample more than requested
        //    //frameBorderSize = (ushort)triggerLevel;

        //    // Workaround for b332:
        //    // sometimes, the cc may be delayed by 10us. The theoretical worst case is each 100us.
        //    // therefore, one tenth of the maximal frame counter value is needed as buffer.
        //    var frameBorderSize = (ushort)(triggerLevel + ushort.MaxValue / 10 + 1);

        //    return (ushort)(ushort.MaxValue - frameBorderSize);
        //}


        #endregion FrequencyResponseLogic helpers


        #endregion Button handler methods

        #region Menu handler methods

        void OnExitMenuItemClick(object sender, EventArgs e) => Close();
        #endregion Menu handler methods

        #region Timer methods
        //void OnTimerTick(object sender, EventArgs e) => ReadPosition();

        #endregion Timer methods


    }
}
