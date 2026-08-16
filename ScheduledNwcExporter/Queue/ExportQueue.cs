using System;
using System.Collections.Generic;
using System.Linq;
using ScheduledNwcExporter.Configuration;

namespace ScheduledNwcExporter.Queue
{
    public class ExportQueue
    {
        private readonly List<ModelExportJob> _jobs;

        public ExportQueue(IEnumerable<ModelExportJob> jobs)
        {
            _jobs = jobs?.ToList() ?? new List<ModelExportJob>();
        }

        public IEnumerable<ModelExportJob> GetActiveJobs()
        {
            return _jobs.Where(j => j.IsEnabled);
        }

        public IEnumerable<ModelExportJob> GetAllJobs()
        {
            return _jobs;
        }

        public void AddJob(ModelExportJob job)
        {
            _jobs.Add(job);
        }

        public void RemoveJob(string jobId)
        {
            _jobs.RemoveAll(j => j.Id == jobId);
        }

        public void Clear()
        {
            _jobs.Clear();
        }
    }
}
