using System;
using System.Windows.Threading;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Scheduler
{
    public class ScheduleManager
    {
        private readonly AppSettings _settings;
        private readonly ILogger _logger;
        private readonly DispatcherTimer _timer;
        private DateTime? _lastTriggeredDate;

        public event EventHandler? ScheduledTimeReached;

        public ScheduleManager(AppSettings settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _timer.Tick += Timer_Tick;
        }

        public void Start()
        {
            _timer.Start();
            _logger.Info("Scheduler", "ScheduleManager started. Monitoring schedule time.");
        }

        public void Stop()
        {
            _timer.Stop();
            _logger.Info("Scheduler", "ScheduleManager stopped.");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_settings.Scheduler.IsSchedulerEnabled) return;

            DateTime now = DateTime.Now;
            int targetHour = _settings.Scheduler.ScheduledHour;
            int targetMinute = _settings.Scheduler.ScheduledMinute;

            if (now.Hour == targetHour && now.Minute == targetMinute)
            {
                // Ensure we only trigger once per minute/day
                if (_lastTriggeredDate == null || _lastTriggeredDate.Value.Date != now.Date || _lastTriggeredDate.Value.Hour != now.Hour || _lastTriggeredDate.Value.Minute != now.Minute)
                {
                    _lastTriggeredDate = now;
                    _logger.Info("Scheduler", $"Scheduled execution time reached ({targetHour:D2}:{targetMinute:D2}). Triggering export session.");
                    ScheduledTimeReached?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
