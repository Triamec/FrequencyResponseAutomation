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
using System.Threading.Tasks;

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

        #region Frequency Response measurement parameters
        int measurementFrequency = 100000; // [Hz]
        int minimumFrequency = 300; // [Hz]
        int maximumFrequency = 400; // [Hz]
        int numberOfSamples = 3; // [-]
        FrequencySpacing frequencySpacing = FrequencySpacing.Optimized;
        string selectedMethod = "Closed Loop";
        double[] excitationLimits = new double[] { 13.8, 0.5, 0.5 };
        #endregion Frequency Response measurement parameters


        #region Hello world code
        /// <summary>
        /// The configuration file for simulated mode.
        /// </summary>
        //const string ConfigurationPath = "HelloWorld.TAMcfg";

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

            // Add the local TAM system on this PC to the topology.
            system = _topology.AddLocalSystem();

            // Boot the Tria-Link so that it learns about connected stations.
            system.Identify();


            // Find the axis with the configured name in the Tria-Link.
            // The AsDepthFirstLeaves extension method performs a tree search an returns all instances of type TamAxis.
            // "Leaves" means that the search doesn't continue within TamAxis nodes.
            _axis = system.AsDepthFirstLeaves<TamAxis>().FirstOrDefault(a => a.Name == AxisName);
            if (_axis == null) throw new TamException(Resources.NoAxisMessage);

            // Most drives get integrated into a real time control system. Accessing them via TAM API like we do here is considered
            // a secondary use case. Tell the axis that we're going to take control. Otherwise, the axis might reject our commands.
            // You should not do this, though, when this application is about to access the drive via the PCI interface.
            _axis.ControlSystemTreatment.Override(enabled: true);

            // Get the register layout of the axis
            // and cast it to the RLID-specific register layout.
            var register = (Axis)_axis.Register;

            // Cache the position unit.
            _unit = register.Parameters.PositionController.PositionUnit.Read().ToString();

            _axis.Drive.AddStateObserver(this);

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
            // Does not contain any asserts, but ensures the principal Frequency Response acquirement mechanism is tested
            System.Diagnostics.Debug.WriteLine("Starting measure");


            #region Setup special culture
            var culture = new CultureInfo(CultureInfo.InvariantCulture.LCID, false);
            Thread thread = Thread.CurrentThread;
            CultureInfo backupCulture = thread.CurrentCulture;
            thread.CurrentCulture = culture;
            #endregion Setup special culture

            try {
                FrequencyResponseAxis axis = SetupFrequencyResponseAxis();

                var signal = new AutoResetEvent(initialState: false);

                Func<AutoResetEvent, IFrequencyResponseAxis, CultureInfo, IFrequencyResponseLogic> worker = StartFrequencyResponse;
                IAsyncResult asyncResult = worker.BeginInvoke(signal, axis, culture, null, null);
                try {
                    var wait = new TimeSpan(0, 0, 3, 0, 0);
                    if (!signal.WaitOne(wait, false)) {
                        Console.WriteLine(string.Format("The test duration exceeded {0} minutes", wait.TotalMinutes));
                        return;
                    }
                } finally {
                    IFrequencyResponseLogic logic = worker.EndInvoke(asyncResult);
                    logic.GetFrequencyResponseResultCancel();
                    logic.Dispose();
                    axis.Tidy();
                    string resultFile = _callback.ResultFile;

                }
            } finally {
                thread.CurrentCulture = backupCulture;
            }
        }

        async Task StartBackAndForthMove(CancellationToken cancellationToken) {
            System.Diagnostics.Debug.WriteLine("Starting back and forth move");

            float backAndForthDistance = 120;
            float backAndForthVelocity = 30;
            TimeSpan moveTimeout = new TimeSpan(0, 0, 10);
            while(!cancellationToken.IsCancellationRequested) {
                await _axis.MoveRelative(backAndForthDistance / 2, backAndForthVelocity).WaitForSuccessAsync(moveTimeout);
                await _axis.MoveRelative(-backAndForthDistance / 2, backAndForthVelocity).WaitForSuccessAsync(moveTimeout);
            }
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

        async void OnMeasureButtonClick(object sender, EventArgs e) {
            try {
                _measureButton.Enabled = false;
                System.Diagnostics.Debug.WriteLine("Measurement button clicked");


                CancellationTokenSource cts = new CancellationTokenSource();
                CancellationToken cancellationToken = cts.Token;

                //await StartBackAndForthMove();
                //Measure();

                Task moveTask = StartBackAndForthMove(cancellationToken);
                Task measureTask = Task.Run(() => Measure());

                //Task moveTask = StartBackAndForthMove(cancellationToken);
                //Task measureTask = Measure();

                System.Diagnostics.Debug.WriteLine("Waiting for measureTask");
                await measureTask;
                cts.Cancel();


            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.MoveErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            } finally {
                _measureButton.Enabled = true;
            }
        }

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
            var parameters = new FrequencyResponseParameters(axis) {
                FrequencyRange = new NIRange(minimumFrequency, maximumFrequency),
                FrequencySteps = numberOfSamples,
                Spacing = frequencySpacing,
                SettlingTime = TimeSpan.FromSeconds(0.2),
            };
            for (int i = 0; i < excitationLimits.Length; i++) {
                parameters.SetMeasuringPointMaximum(i, excitationLimits[i]);
            }


            _callback = new FrequencyResponseLogicCallback(logic, signal, formatProvider);
            string desiredMethod = selectedMethod;
            var methods = FrequencyResponseConfig.Read()
                                   .MeasurementMethods
                                   .Where(axis.SupportsMethod)
                                   .ToArray();
            axis.MeasurementMethod = methods.Single(method => method.Name == desiredMethod);
            axis.SamplingTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / measurementFrequency);
            logic.GetFrequencyResponseResultAsync(axis, parameters);
            return logic;
        }

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
