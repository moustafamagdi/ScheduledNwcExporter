using System;
using System.Linq;
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

            // 1. Check legacy slot for backward compatibility
            CheckAndTriggerSlot(_settings.Scheduler.ScheduledHour, _settings.Scheduler.ScheduledMinute, null, now);

            // 2. Check all modern slots
            if (_settings.Scheduler.Slots != null)
            {
                foreach (var slot in _settings.Scheduler.Slots)
                {
                    if (slot.IsEnabled && slot.Days.Contains(now.DayOfWeek))
                    {
                        CheckAndTriggerSlot(slot.Hour, slot.Minute, slot, now);
                    }
                }
            }
        }

        private void CheckAndTriggerSlot(int targetHour, int targetMinute, ScheduleSlot? slot, DateTime now)
        {
            if (now.Hour == targetHour && now.Minute == targetMinute)
            {
                // Ensure we only trigger once per minute/day for this specific time
                if (_lastTriggeredDate == null || 
                    _lastTriggeredDate.Value.Date != now.Date || 
                    _lastTriggeredDate.Value.Hour != now.Hour || 
                    _lastTriggeredDate.Value.Minute != now.Minute)
                {
                    _lastTriggeredDate = now;
                    string source = slot != null ? "Multi-slot" : "Legacy-slot";
                    _logger.Info("Scheduler", $"Scheduled execution time reached ({targetHour:D2}:{targetMinute:D2}) via {source}. Triggering export session.");
                    ScheduledTimeReached?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
