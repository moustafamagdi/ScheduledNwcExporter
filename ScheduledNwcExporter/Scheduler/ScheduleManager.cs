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
            var modernSlots = _settings.Scheduler.Slots?
                .Where(slot => slot != null)
                .ToList();

            // Modern schedule slots are authoritative once at least one slot exists.
            // The legacy hour/minute is retained strictly as a migration fallback for
            // older configuration files that do not contain any modern slots.
            if (modernSlots != null && modernSlots.Count > 0)
            {
                foreach (var slot in modernSlots)
                {
                    if (slot.IsEnabled && slot.Days != null && slot.Days.Contains(now.DayOfWeek))
                    {
                        CheckAndTriggerSlot(slot.Hour, slot.Minute, slot, now);
                    }
                }
                return;
            }

            CheckAndTriggerSlot(_settings.Scheduler.ScheduledHour, _settings.Scheduler.ScheduledMinute, null, now);
        }

        private void CheckAndTriggerSlot(int targetHour, int targetMinute, ScheduleSlot? slot, DateTime now)
        {
            if (now.Hour != targetHour || now.Minute != targetMinute) return;

            // Ensure we only trigger once per minute/day for the current schedule time.
            if (_lastTriggeredDate == null ||
                _lastTriggeredDate.Value.Date != now.Date ||
                _lastTriggeredDate.Value.Hour != now.Hour ||
                _lastTriggeredDate.Value.Minute != now.Minute)
            {
                _lastTriggeredDate = now;
                string source = slot != null ? "Schedule-slot" : "Legacy-slot";
                _logger.Info("Scheduler", $"Scheduled execution time reached ({targetHour:D2}:{targetMinute:D2}) via {source}. Triggering export session.");
                ScheduledTimeReached?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
