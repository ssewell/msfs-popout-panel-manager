﻿using MSFSPopoutPanelManager.Orchestration;
using MSFSPopoutPanelManager.Shared;
using System;
using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows.Media;

namespace MSFSPopoutPanelManager.MainApp.ViewModel
{
    public abstract class BaseViewModel : ObservableObject
    {
        protected const string ROOT_DIALOG_HOST = "RootDialog";

        protected MainOrchestrator Orchestrator { get; set; }

        public BaseViewModel(MainOrchestrator orchestrator)
        {
            Orchestrator = orchestrator;

            Orchestrator.PanelPopOut.OnPopOutStarted += (sender, e) => IsDisabledAppInput = true;
            Orchestrator.PanelPopOut.OnPopOutCompleted += (sender, e) => IsDisabledAppInput = false;
            Orchestrator.PanelSource.OnPanelSourceSelectionStarted += (sender, e) => IsDisabledAppInput = true;
            Orchestrator.PanelSource.OnPanelSourceSelectionCompleted += (sender, e) => IsDisabledAppInput = false;
        }

        public AppSettingData AppSettingData => Orchestrator.AppSettingData;

        public ProfileData ProfileData => Orchestrator.ProfileData;

        public FlightSimData FlightSimData => Orchestrator.FlightSimData;

        public bool IsDisabledAppInput { get; set; }

        protected List<Run> FormatStatusMessages(List<StatusMessage> messages)
        {
            List<Run> runs = new List<Run>();

            foreach (var statusMessage in messages)
            {
                var run = new Run();
                run.Text = statusMessage.Message;

                switch (statusMessage.StatusMessageType)
                {
                    case StatusMessageType.Success:
                        run.Foreground = new SolidColorBrush(Colors.LimeGreen);
                        break;
                    case StatusMessageType.Failure:
                        run.Foreground = new SolidColorBrush(Colors.IndianRed);
                        break;
                    case StatusMessageType.Info:
                        break;
                }

                runs.Add(run);

                if (statusMessage.NewLine)
                    runs.Add(new Run { Text = Environment.NewLine });
            }

            return runs;
        }
    }
}
