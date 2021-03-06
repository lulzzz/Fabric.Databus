﻿using ElasticSearchSqlFeeder.Interfaces;
using ElasticSearchSqlFeeder.Shared;
using Fabric.Databus.Config;
using Fabric.Databus.Domain.Jobs;

namespace Fabric.Databus.Domain.Importers
{
    public interface IImportRunner
    {
        void ReadFromDatabase(Job config, IProgressMonitor progressMonitor, IJobStatusTracker jobStatusTracker);
    }
}
