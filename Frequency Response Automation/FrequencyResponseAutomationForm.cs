using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Triamec.FrequencyResponseAnalysis;
using Triamec.FrequencyResponseAnalysis.Configuration;
using Triamec.Tam.FrequencyResponseAnalysis;
using Triamec.Tam.Requests;
using Triamec.Tam.Samples.Properties;
using Triamec.TriaLink;
// Rlid19 represents the register layout of drives of the current generation. A previous generation drive has layout 4.
using Axis = Triamec.Tam.Rlid19.Axis;

namespace Triamec.Tam.Samples {
    /// <summary>
    /// The main form of the TAM "Frequency Response Automation" application.
    /// </summary>
    internal partial class FrequencyResponseAutomationForm : Form {
        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="FrequencyResponseAutomationForm"/> class.
        /// </summary>
        public FrequencyResponseAutomationForm() {
            InitializeComponent();
        }
        #endregion Constructor

        #region Frequency Response measurement parameters

        const int MeasurementFrequency = 100000; // [Hz]
        const int MinimumFrequency = 300; // [Hz]
        const int MaximumFrequency = 400; // [Hz]
        const int NumberOfSamples = 3; // [-]
        static readonly FrequencySpacing FrequencySpacing = FrequencySpacing.Optimized;
        const string SelectedMethod = "Closed Loop";
        static readonly double[] ExcitationLimits = new double[] { 13.8, 0.5, 0.5 };
        static readonly double[] MeasurementPositions = new double[] { 30.0, 90.0, 120.0 };
        const float MoveToPositionVelocity = 60;
        static readonly bool DoBackAndForthMove = true;
        const float BackAndForthDistance = 30;
        const float BackAndForthVelocity = 10;

        #endregion Frequency Response measurement parameters


        #region Frequency Response Automation code
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

            _axis.Drive.AddStateObserver(this);

            // Start displaying the position in regular intervals.
            _timer.Start();
        }

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
            _axis.MoveRelative(Math.Sign(sign) * Distance);

        async Task Measure() {
            // Does not contain any asserts, but ensures the principal Frequency Response acquirement mechanism is tested
            Debug.WriteLine("Starting measure");

            var controlSystem = SetupControlSystem();

            var (measurement, task) = StartFrequencyResponse(controlSystem);
            var wait = new TimeSpan(0, 0, 3, 0, 0);
            try {
                await TimeoutAfter(task, wait);
            } catch (TimeoutException) {
                measurement.MeasureFrequencyResponseCancel();
                Console.WriteLine(string.Format("The test duration exceeded {0} minutes", wait.TotalMinutes));
                return;
            } finally {
                measurement.Dispose();
                controlSystem.Tidy();
            }

        }

        async Task WaitAndAssertRequest(TamRequest request, TimeSpan moveTimeout) {
            var terminated = await request.WaitForTerminationAsync(moveTimeout);
            if (!terminated) { throw new TimeoutException("Move duration exceeded."); }
            switch (request.Termination) {
                case TamRequestResolution.Completed:
                case TamRequestResolution.Superseded:

                    // reprogramming comes from Stop, this is OK
                    break;

                default:
                    throw new CommandRejectedException("Move request termination was " + request.Termination);
            }
        }

        async Task MoveBackAndForth(CancellationToken cancellationToken, float backAndForthDistance, float backAndForthVelocity) {
            Debug.WriteLine("Starting back and forth move");
            var register = (Axis)_axis.Register;
            float currentReferencePosition = register.Signals.PathPlanner.PositionFloat.Read();
            TimeSpan moveTimeout = new TimeSpan(0, 0, 10);
            while (!cancellationToken.IsCancellationRequested) {
                var request = _axis.MoveAbsolute(currentReferencePosition + backAndForthDistance / 2,
                                                 backAndForthVelocity, PathPlannerDirection.Positive);
                await WaitAndAssertRequest(request, moveTimeout);
                if (cancellationToken.IsCancellationRequested) break;

                request = _axis.MoveAbsolute(currentReferencePosition - backAndForthDistance / 2,
                                             -backAndForthVelocity, PathPlannerDirection.Negative);
                await WaitAndAssertRequest(request, moveTimeout);
            }
        }

        #endregion Frequency Response Automation code

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

                for (int i = 0; i < MeasurementPositions.Length; i++) {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    CancellationToken cancellationToken = cts.Token;

                    await _axis.MoveAbsolute(MeasurementPositions[i], MoveToPositionVelocity).WaitForSuccessAsync(TimeSpan.FromSeconds(10));

                    Task moveTask;
                    if (DoBackAndForthMove) {
                        if (SelectedMethod == "Closed Loop") {
                            moveTask = MoveBackAndForth(cancellationToken, BackAndForthDistance, BackAndForthVelocity);
                        } else {
                            throw new Exception("Back and Forth move is only possible in Closed Loop");
                        }
                    } else {
                        moveTask = null;
                    }
                    Task measureTask = Measure();

                    await measureTask;
                    cts.Cancel();
                    await _axis.Stop(false).WaitForSuccessAsync(TimeSpan.FromSeconds(10));
                    if (moveTask != null) {
                        await moveTask;
                    }
                }

            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.MoveErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            } finally {
                _measureButton.Enabled = true;
            }
        }

        IControlSystem SetupControlSystem() {

            var result = _axis.AsControlSystem();

            // open loop
            result.MeasurementMethod = FrequencyResponseConfig.Read().MeasurementMethods[0];

            return result;
        }
        #endregion SetupFrequencyResponseAxis

        #region FrequencyResponseLogic helpers
        /// <summary>
        /// Create the Frequency Response logic on another thread than the thread who will wait for completion
        /// </summary>
        /// <param name="system">The axis.</param>
        /// <param name="log">The log.</param>
        /// <returns>
        /// The created resource that must be managed by the caller.
        /// </returns>
        (FrequencyResponseMeasurement measurement, Task task) StartFrequencyResponse(IControlSystem system) {
            var measurement = new FrequencyResponseMeasurement();
            var parameters = new FrequencyResponseMeasurementParameters(system) {
                FrequencyRangeMinimum = MinimumFrequency,
                FrequencyRangeMaximum = MaximumFrequency,
                FrequencySteps = NumberOfSamples,
                Spacing = FrequencySpacing,
                SettlingTime = TimeSpan.FromSeconds(0.2),
            };
            for (int i = 0; i < ExcitationLimits.Length; i++) {
                parameters.SetMeasuringPointMaximum(i, ExcitationLimits[i]);
            }

            var tcs = new TaskCompletionSource<object>();

            new FrequencyResponseMeasurementCallback(tcs, measurement);
            string desiredMethod = SelectedMethod;
            var methods = FrequencyResponseConfig.Read()
                                   .MeasurementMethods
                                   .Where(system.SupportsMethod)
                                   .ToArray();
            system.MeasurementMethod = methods.Single(method => method.Name == desiredMethod);
            system.SamplingTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / MeasurementFrequency);
            measurement.MeasureFrequencyResponseAsync(system, parameters);
            return (measurement, tcs.Task);
        }

        #endregion FrequencyResponseLogic helpers

        /// <summary>
        /// Creates a new <see cref="Task"/> that completes if this <see cref="Task"/> completes or a specified duration
        /// elapses.
        /// </summary>
        /// <param name="task">The task to wait for.</param>
        /// <param name="timeout">The duration to maximally wait for <paramref name="task"/>s completion.</param>
        /// <exception cref="TimeoutException">A timeout occurred.</exception>
        // TODO unit testing
        static async Task TimeoutAfter(Task task, TimeSpan timeout) {
            using (var timeoutCancellationTokenSource = new CancellationTokenSource()) {
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token))
                                              .ConfigureAwait(continueOnCapturedContext: false);

                if (completedTask == task) {
                    timeoutCancellationTokenSource.Cancel();

                    // propagate exception rather than AggregateException, if calling task.Result.
                    await task.ConfigureAwait(continueOnCapturedContext: false);
                } else {
                    throw new TimeoutException();
                }
            }
        }

        #endregion Button handler methods

        #region Menu handler methods

        void OnExitMenuItemClick(object sender, EventArgs e) => Close();
        #endregion Menu handler methods

        #region Timer methods
        //void OnTimerTick(object sender, EventArgs e) => ReadPosition();

        #endregion Timer methods


    }
}
