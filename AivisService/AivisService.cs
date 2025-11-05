using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Runtime.InteropServices;
using System.Configuration;
using System.Management;
using Thermo.Chromeleon.Sdk.Common;
using Thermo.Chromeleon.Sdk.Interfaces.Data;
using Thermo.Chromeleon.Sdk.Interfaces.Instruments;
using Thermo.Chromeleon.Sdk.Utilities;
using Thermo.Chromeleon.Sdk.Interfaces;
using Thermo.Chromeleon.Sdk.UserManagement;
using AivisService.Core.Dtos;
using AivisService.Core;
using System.Threading;


namespace AivisService
{
    public partial class AivisService : ServiceBase
    {
        private int eventId = 1;
        int ServiceUserId = int.Parse(ConfigurationManager.AppSettings["ServiceUser"]);
        string SequenceNotSubmittedTrailType = ConfigurationManager.AppSettings["SequenceNotSubmittedTrailType"];
        string SequenceNotReviewedTrailType = ConfigurationManager.AppSettings["SequenceNotReviewedTrailType"];
        string SiteCode = ConfigurationManager.AppSettings["SiteCode"];
        int SiteId = 0;
        System.Timers.Timer timer = null;

        public AivisService()
        {
            InitializeComponent();
            eventLog1 = new EventLog();
            if (!EventLog.SourceExists("AivisSource"))
            {
                EventLog.CreateEventSource("AivisSource", "");
            }
            eventLog1.Source = "AivisSource";
            eventLog1.Log = "";
        }

        protected override void OnStart(string[] args)
        {
            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            eventLog1.WriteEntry("In OnStart.");

            SiteId = new DatabaseService().CheckAndRegisterClient(SiteCode, GetHDDSerialNo());

            if (SiteId != 0)
            {
                timer = new System.Timers.Timer
                {
                    Interval = 60000 // 60 seconds
                };
                timer.Elapsed += new ElapsedEventHandler(this.OnTimer);
                timer.Start();
            }
            else
            {
                eventLog1.WriteEntry("Site ID Not Found... Service is in ideal state now..");
            }

            //must be on end of start
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        public void OnTimer(object sender, ElapsedEventArgs args)
        {
            // TODO: Insert monitoring activities here.
            eventLog1.WriteEntry("Started Monitoring The Chromeleon Critical Exception", EventLogEntryType.Information, eventId++);

            Thread staThread = new Thread(() =>
            {
                try
                {
                    ProcessBackService();
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry("Chromeleon Service error: " + ex.Message);
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(System.IntPtr handle, ref ServiceStatus serviceStatus);

        protected override void OnStop()
        {
            // Update the service state to Stop Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            eventLog1.WriteEntry("In OnStop.");


            // Update the service state to Stopped.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        public string GetHDDSerialNo()
        {
            string hddSerialNo = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
            ManagementObjectCollection mbsList = searcher.Get();

            foreach (ManagementObject mo in mbsList)
            {
                if (mo["SerialNumber"] != null)
                {
                    hddSerialNo = mo["SerialNumber"].ToString();
                    break; // Assuming only one HDD serial number is needed
                }
            }
            return hddSerialNo;
        }

        public void ProcessBackService()
        {
            eventLog1.WriteEntry("Inside Proessing Sequences", EventLogEntryType.Information, eventId++);

            //define scope
            using (var scope = new CmSdkScope())
            {
                //Ligon
                CmSdk.Logon.DoLogon();

                //check if login success ?
                if (CmSdk.Logon.IsLoggedOn)
                {
                    eventLog1.WriteEntry("Chomeleon Logged In Done...", EventLogEntryType.Information, eventId++);

                    var itemFactory = CmSdk.GetItemFactory();

                    foreach (var server in itemFactory.DataVaultServers)
                    {
                        int ServerId = new DatabaseService().ManageChromeServe(server, SiteId, ServiceUserId);

                        //get server
                        eventLog1.WriteEntry("Server: " + server.Name + "   Server URL: " + server.Url, EventLogEntryType.Information, eventId++);

                        //get server and data vault
                        foreach (var datavault in server.DataVaults)
                        {
                            //int DataVaultId = new DatabaseService().ManageDataVault(datavault, ServerId, SiteId, ServiceUserId);
                            //Console.WriteLine("Data Vault: " + datavault.Name + "   Data Vault URL: " + datavault.Url);

                            //ProcessDataVault(datavault, DataVaultId, itemFactory);


                            foreach (var item in datavault.Children)
                            {
                                Console.WriteLine(item.Name);
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Login Failed...");
                }
            }

            Console.ReadLine();
        }

        private void GetInstrumentMethod(IDataVault datavault)
        { 
            
        }

        private void ProcessDataVault(IDataVault datavault, int DataVaultId, IItemFactory itemFactory)
        {
            if (datavault != null)
            {
                var Folders = datavault.Children.Where(w => w.GetType().Name == "Folder").ToList();

                foreach (var folder in Folders)
                {
                    if (folder.Name != "$RecycleBin$")
                    {
                        foreach (var item in folder.ItemAuditTrail)
                        {
                            if (item.Operation.ToString() == "Moved" && item.ItemType.Name == "ISequence")
                            {
                                //Console.WriteLine("Inside Deleted Folder");

                                var TestItem = item.RelativePath;
                                //var TestSeq = item.RelativePath as ISequence;

                                var DeleteFolder = Folders.Where(w => w.Name == "$RecycleBin$").FirstOrDefault();
                            }

                        }
                    }

                    ProcessSequence(folder.Url, DataVaultId);
                }

                ProcessSequence(datavault.Url, DataVaultId);


            }
        }

        private void ProcessSequence(Uri uri, int DataVaultId)
        {
            var itemFactory = CmSdk.GetItemFactory();

            //get Child data, ex: sequence
            if (itemFactory.TryGetItem(uri, out IDataItem dataItem))
            {
                IParentItem parentItem = dataItem as IParentItem;
                if (parentItem != null)
                {
                    foreach (var child in parentItem.Children)
                    {
                        var parentChildItem = child as IParentItem;

                        if (parentChildItem is ISequence) // && child.Name == "QC046CARV_DS_1406A"
                        {

                            //Uncomment this
                            //Console.WriteLine("Child Name : " + child.Name + "   Child URL: " + child.Url + "  Type: " + child.GetType());

                            SequenceDetails sequenceDetails = new SequenceDetails();
                            sequenceDetails.DataVaultId = DataVaultId;
                            sequenceDetails.SequenceNumber = parentChildItem.Name;
                            sequenceDetails.SequenceUri = parentChildItem.Url.ToString();
                            sequenceDetails.ImportedBy = ServiceUserId;
                            sequenceDetails.ImportedOn = DateTime.Now;
                            sequenceDetails.IsActive = true;
                            sequenceDetails.SiteId = SiteId;

                            int SequenceId = new DatabaseService().ManageSequence(sequenceDetails);

                            var CustomSeq = parentChildItem as ISequence;


                            GetSequenceCustomInfo(parentChildItem as ISequence, out string SeqBatchNo, out string SeqProductName, out string SeqTestName, out string ARNo, out string InstrumentId);

                            #region New Code Working

                            foreach (var atril in parentChildItem.ItemAuditTrail)
                            {
                                if (atril.Operation.ToString() == "AbortedRun")
                                {
                                    // Aborted Tun Manual & Instrument
                                    HandleAbortedRunInjection(SequenceId, parentChildItem as ISequence);
                                }
                                else if (atril.Operation.ToString() == "Deleted" && atril.ItemType.Name == "IInjection") //add condition of "Raw Data Contained"
                                {
                                    // Deleted Injection
                                    ManageTrails(SequenceId, atril.Number, atril.Description, "DeletedInjection", SeqBatchNo, SeqProductName, SeqTestName, atril.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, atril.TransactionLogEntry.User.Name);
                                }

                                //else if (atril.Operation.ToString() == "Changed" && atril.ItemType.Name == "IProcessingMethod")
                                //{
                                //    //Changed
                                //    var Injections = (parentChildItem as ISequence).Injections;

                                //    if (Injections.Any())
                                //    {
                                //        IProcessingMethod processingMethod = Injections[0].ProcessingMethod;
                                //        foreach (var component in processingMethod.Components)
                                //        {
                                //            if (component.CustomFields.Where(w => w.Definition.Name.StartsWith("STD") && w.Definition.Name.EndsWith("WT")).Count() > 0)
                                //            {
                                //                ManageTrails(SequenceId, atril.Number, atril.Description, "Changed", SeqBatchNo, SeqProductName, SeqTestName, atril.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, atril.TransactionLogEntry.User.Name);
                                //            }
                                //        }

                                //    }
                                //}



                                //else if (atril.Operation.ToString() == "Moved")
                                //{
                                //    // Manual Integration
                                //    ManageTrails(SequenceId, atril.Number, atril.Description, "Moved", SeqBatchNo, SeqProductName, SeqTestName, atril.TransactionLogEntry.StartTime.LocalDateTime, ARNo);
                                //}
                            }



                            ////Check deleted Injection
                            //ISequence currentSequence = (parentChildItem as ISequence);
                            //foreach (var field in currentSequence.CustomFields)
                            //{
                            //    Console.WriteLine(field.ToString());
                            //}

                            var ChromatogramTrail = parentChildItem.ItemAuditTrail.Where(w => w.ItemType.Name == "IChromatogram").OrderByDescending(o => o.Number).FirstOrDefault();
                            if (ChromatogramTrail != null)
                            {
                                ManageTrails(SequenceId, ChromatogramTrail.Number, ChromatogramTrail.Description, "ManualIntegration", SeqBatchNo, SeqProductName, SeqTestName, ChromatogramTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, ChromatogramTrail.TransactionLogEntry.User.Name);
                            }


                            var LastFinishRunTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "FinishedRun").OrderByDescending(o => o.TransactionLogEntry.StartTime.LocalDateTime).FirstOrDefault();
                            if (LastFinishRunTrail != null)
                            {
                                //Check Change Injection
                                //var ChangedInjectionsTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Changed" && w.ItemType.Name == "ISequence" && w.TransactionLogEntry.StartTime.LocalDateTime > LastFinishRunTrail.TransactionLogEntry.StartTime.LocalDateTime).ToList();
                                var ChangedInjectionsTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Changed" && w.ItemType.Name == "ISequence").ToList();
                                if (ChangedInjectionsTrails.Any())
                                {
                                    foreach (var chTrail in ChangedInjectionsTrails)
                                    {
                                        //Change Injection
                                        ManageTrails(SequenceId, chTrail.Number, chTrail.Description, "ChangedInjection", SeqBatchNo, SeqProductName, SeqTestName, chTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, chTrail.TransactionLogEntry.User.Name);
                                    }
                                }

                                var RenamedInjectionsTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString().ToLower().Contains("ren") && w.ItemType.Name == "ISequence" && w.TransactionLogEntry.StartTime.LocalDateTime > LastFinishRunTrail.TransactionLogEntry.StartTime.LocalDateTime).ToList();
                                if (RenamedInjectionsTrails.Any())
                                {
                                    foreach (var renTrail in RenamedInjectionsTrails)
                                    {
                                        //Rename Injection
                                        ManageTrails(SequenceId, renTrail.Number, renTrail.Description, "RenamedInjection", SeqBatchNo, SeqProductName, SeqTestName, renTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, renTrail.TransactionLogEntry.User.Name);
                                    }
                                }

                                //Changed Exception

                                var Injections = (parentChildItem as ISequence).Injections;

                                var ChangedTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Changed" && w.ItemType.Name == "IProcessingMethod" && w.TransactionLogEntry.StartTime.LocalDateTime > LastFinishRunTrail.TransactionLogEntry.StartTime.LocalDateTime).ToList();
                                if (ChangedTrails.Any() && Injections.Any())
                                {
                                    foreach (var chTrail in ChangedTrails)
                                    {
                                        IProcessingMethod processingMethod = Injections[0].ProcessingMethod;

                                        foreach (var component in processingMethod.Components)
                                        {
                                            if (component.CustomFields.Where(w => w.Definition.Name.StartsWith("STD_") && w.Definition.Name.EndsWith("_WT")).Count() > 0)
                                            {
                                                ManageTrails(SequenceId, chTrail.Number, chTrail.Description, "Changed", SeqBatchNo, SeqProductName, SeqTestName, chTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, chTrail.TransactionLogEntry.User.Name);
                                            }
                                        }



                                        //foreach (var def in processingMethod.Components.ConcentrationLevelDefinitions)
                                        //{
                                        //    Console.WriteLine(def.Description + "-" + def.Name);
                                        //}
                                    }
                                }

                            }


                            // check Submitted Trails Exists
                            var LastSubmitTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Submitted").OrderByDescending(o => o.Number).FirstOrDefault();

                            if (LastSubmitTrail != null)
                            {
                                // mark delete any not Submitted Trails found for Sequence
                                new DatabaseService().DeleteSubmittedReviewedTrail(SequenceId, SequenceNotSubmittedTrailType, Constants.DeleteSubmittedTrailMsg);
                            }
                            else
                            {
                                //Not Submitted
                                // make Trail Number 0
                                ManageTrails(SequenceId, 0, string.Empty, "NotSubmitted", SeqBatchNo, SeqProductName, SeqTestName, parentChildItem.Creation.StartTime.LocalDateTime, ARNo, InstrumentId, parentChildItem.ItemAuditTrail[0].TransactionLogEntry.User.Name);
                            }

                            // check reviewed trails exists
                            var FirstReviewedTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Reviewed").OrderBy(o => o.Number).FirstOrDefault();
                            if (FirstReviewedTrail == null)
                            {
                                // Not Reviwed
                                // make Trail Number 0
                                ManageTrails(SequenceId, 0, string.Empty, "NotReviewed", SeqBatchNo, SeqProductName, SeqTestName, parentChildItem.Creation.StartTime.LocalDateTime, ARNo, InstrumentId, parentChildItem.ItemAuditTrail[0].TransactionLogEntry.User.Name);
                            }
                            else
                            {
                                // mark delete any not Submitted Trails found for Sequence
                                new DatabaseService().DeleteSubmittedReviewedTrail(SequenceId, SequenceNotReviewedTrailType, Constants.DeleteReviewedTrailMsg);

                                // if signature removed found after reviewed
                                var SignatureRemovedTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "SignatureRemoved" && w.Number > FirstReviewedTrail.Number).FirstOrDefault();
                                if (SignatureRemovedTrail != null)
                                {
                                    //signature Removed
                                    ManageTrails(SequenceId, SignatureRemovedTrail.Number, SignatureRemovedTrail.Description, "SignatureRemoved", SeqBatchNo, SeqProductName, SeqTestName, SignatureRemovedTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, SignatureRemovedTrail.TransactionLogEntry.User.Name);
                                }
                            }

                            #endregion

                            #region repeat batch Sequence
                            if (ARNo != "NA" && ARNo != "N.A" && ARNo != null && SeqTestName != "NA" && SeqTestName != "N.A" && SeqTestName != null)
                            {
                                ManageRepeatSequenceTrails(SequenceId, 0, string.Empty, "RepeatSequence", SeqBatchNo, SeqProductName, SeqTestName, DateTime.Now, ARNo, InstrumentId, parentChildItem.ItemAuditTrail[0].TransactionLogEntry.User.Name);
                            }

                            #endregion
                        }
                    }
                }
            }
        }


        void HandleAbortedRunInjection(int SequenceId, ISequence seq)
        {
            var injectionAccess = CmSdk.GetInstrumentAccess();
            foreach (var inject in seq.Injections)
            {
                var InterruptedInjections = seq.Injections.Where(w => w.Status.ToString() == "Interrupted").ToList();

                if (InterruptedInjections.Any())
                {
                    foreach (var intrinject in InterruptedInjections)
                    {
                        IAuditTrail trail = intrinject.AuditTrail;
                        foreach (var message in trail.Messages)
                        {
                            if (message.Message.ToLower().Contains("stopping the sequence queue (immediately)"))
                            {
                                GetInjectionCustomInfo(intrinject, out string BatchNo, out string ProductName, out string TestName, out string ARNo);
                                ManageInjectionTrails(SequenceId, intrinject.AuditTrail.Id, intrinject.Comment.ToString(), "AbortedRun", BatchNo, ProductName, TestName, intrinject.LastUpdate.StartTime.LocalDateTime, ARNo, seq.Instrument, intrinject.ItemAuditTrail[0].TransactionLogEntry.User.Name, intrinject.Name);
                            }

                            if (message.Message.Contains("Instrument method aborted"))
                            {
                                GetInjectionCustomInfo(intrinject, out string BatchNo, out string ProductName, out string TestName, out string ARNo);
                                ManageInjectionTrails(SequenceId, intrinject.AuditTrail.Id, intrinject.Comment.ToString(), "AbortedRunInstrument", BatchNo, ProductName, TestName, intrinject.LastUpdate.StartTime.LocalDateTime, ARNo, seq.Instrument, intrinject.ItemAuditTrail[0].TransactionLogEntry.User.Name, intrinject.Name);
                            }

                        }

                        //if (intrinject.AuditTrail.Messages.Where(w => w.Message.Contains("Instrument method aborted")).Any())
                        //{
                        //    GetInjectionCustomInfo(intrinject, out string BatchNo, out string ProductName, out string TestName, out string ARNo);
                        //    ManageInjectionTrails(SequenceId, intrinject.AuditTrail.Id, intrinject.Comment.ToString(), "AbortedRunInstrument", BatchNo, ProductName, TestName, intrinject.LastUpdate.StartTime.LocalDateTime, ARNo, intrinject.Name);
                        //}

                        //if (intrinject.AuditTrail.Messages.Me .Where(w => w.Message.Contains("Stopping the sequence queue (Immediately)")).Any())
                        //{
                        //    GetInjectionCustomInfo(intrinject, out string BatchNo, out string ProductName, out string TestName, out string ARNo);
                        //    ManageInjectionTrails(SequenceId, intrinject.AuditTrail.Id, intrinject.Comment.ToString(), "AbortedRun", BatchNo, ProductName, TestName, intrinject.LastUpdate.StartTime.LocalDateTime, ARNo, intrinject.Name);
                        //}

                    }

                }
            }
        }

        void GetInjectionCustomInfo(IInjection Inj, out string BatchNo, out string ProductName, out string TestName, out string ARNo)
        {
            //foreach (var field in Inj.CustomFields)
            //{
            //    ICustomField custonField = field as ICustomField;

            //    if (custonField.Definition.Name == "BATCH_NO")
            //    {
            //        Console.WriteLine(custonField.Definition.Description);
            //    }
            //}

            BatchNo = Inj.CustomFields["BATCH_NO"] == null ? null : Inj.CustomFields["BATCH_NO"].ToString();
            ProductName = Inj.CustomFields["PRODUCT_NAME"] == null ? null : Inj.CustomFields["PRODUCT_NAME"].ToString();
            TestName = Inj.CustomFields["TEST"] == null ? null : Inj.CustomFields["TEST"].ToString();
            ARNo = Inj.CustomFields["AR_NO"] == null ? null : Inj.CustomFields["AR_NO"].ToString();
        }

        void GetSequenceCustomInfo(ISequence Seq, out string BatchNo, out string ProductName, out string TestName, out string ARNo, out string InstrumentId)
        {
            var Injections = Seq.Injections;
            ProductName = null;
            BatchNo = null;
            TestName = null;
            ARNo = null;

            if (Seq.CustomFields.Any())
            {
                foreach (var field in Seq.CustomFields)
                {
                    Console.WriteLine(field.Definition);
                }
            }

            InstrumentId = Seq.Instrument;

            if (Injections.Any() && Injections.CustomFieldDefinitions.Count > 0)
            {
                BatchNo = (Injections.Where(w => w.CustomFields["BATCH_NO"] != null && w.CustomFields["BATCH_NO"].ToString() != "NA" && w.CustomFields["BATCH_NO"].ToString() != "N.A").Select(s => s.CustomFields["BATCH_NO"]).FirstOrDefault()).ToString();

                if (Injections.Where(w => w.CustomFields["PRODUCT_NAME"] != null && w.CustomFields["PRODUCT_NAME"].ToString() != "NA" && w.CustomFields["PRODUCT_NAME"].ToString() != "N.A").Select(s => s.CustomFields["PRODUCT_NAME"]).FirstOrDefault() != null)
                {
                    ProductName = (Injections.Where(w => w.CustomFields["PRODUCT_NAME"] != null && w.CustomFields["PRODUCT_NAME"].ToString() != "NA" && w.CustomFields["PRODUCT_NAME"].ToString() != "N.A").Select(s => s.CustomFields["PRODUCT_NAME"]).FirstOrDefault()).ToString();
                }

                TestName = (Injections.Where(w => w.CustomFields["TEST"] != null && w.CustomFields["TEST"].ToString() != "NA" && w.CustomFields["TEST"].ToString() != "N.A").Select(s => s.CustomFields["TEST"]).FirstOrDefault()).ToString();

                if (Injections.Where(w => w.CustomFields["AR_NO"] != null && w.CustomFields["AR_NO"].ToString() != "NA" && w.CustomFields["AR_NO"].ToString() != "N.A").Select(s => s.CustomFields["AR_NO"]).FirstOrDefault() != null)
                {
                    ARNo = (Injections.Where(w => w.CustomFields["AR_NO"] != null && w.CustomFields["AR_NO"].ToString() != "NA" && w.CustomFields["AR_NO"].ToString() != "N.A").Select(s => s.CustomFields["AR_NO"]).FirstOrDefault()).ToString();
                }

            }


        }

        long ManageTrails(int SequenceId, int TrailNumber, string Description, string InterventionCode, string BatchNo, string ProductName, string TestName, DateTime AddedOn, string ARNo, string InstrumentId, string Operator, string InjectionName = null)
        {
            TrailDetails trailDetails = new TrailDetails();
            trailDetails.SequenceId = SequenceId;
            trailDetails.ChromTrailNumber = TrailNumber;
            trailDetails.Trails = Description;
            trailDetails.ImportedBy = ServiceUserId;
            trailDetails.InterventionCode = InterventionCode;
            trailDetails.BatchNo = BatchNo;
            trailDetails.ProductName = ProductName;
            trailDetails.TestName = TestName;
            trailDetails.AddedOn = AddedOn;
            trailDetails.InjectionName = InjectionName;
            trailDetails.ARNo = ARNo;
            trailDetails.InstrumentId = InstrumentId;
            trailDetails.Operator = Operator;

            long TrailId = new DatabaseService().ManageTrails(trailDetails);

            return TrailId;
        }

        long ManageInjectionTrails(int SequenceId, Guid AuditTrailId, string Description, string InterventionCode, string BatchNo, string ProductName, string TestName, DateTime AddedOn, string ARNo, string InstrumentId, string Operator, string InjectionName = null)
        {
            TrailDetails trailDetails = new TrailDetails();
            trailDetails.SequenceId = SequenceId;
            trailDetails.TrailId = AuditTrailId;
            trailDetails.Trails = Description;
            trailDetails.ImportedBy = ServiceUserId;
            trailDetails.InterventionCode = InterventionCode;
            trailDetails.BatchNo = BatchNo;
            trailDetails.ProductName = ProductName;
            trailDetails.TestName = TestName;
            trailDetails.AddedOn = AddedOn;
            trailDetails.InjectionName = InjectionName;
            trailDetails.ARNo = ARNo;
            trailDetails.InstrumentId = InstrumentId;
            trailDetails.Operator = Operator;

            long TrailId = new DatabaseService().ManageInjectionTrails(trailDetails);

            return TrailId;
        }

        long ManageRepeatSequenceTrails(int SequenceId, int TrailNumber, string Description, string InterventionCode, string BatchNo, string ProductName, string TestName, DateTime AddedOn, string ARNo, string Operator, string InjectionName = null)
        {
            TrailDetails trailDetails = new TrailDetails();
            trailDetails.SequenceId = SequenceId;
            trailDetails.ChromTrailNumber = TrailNumber;
            trailDetails.Trails = Description;
            trailDetails.ImportedBy = ServiceUserId;
            trailDetails.InterventionCode = InterventionCode;
            trailDetails.BatchNo = BatchNo;
            trailDetails.ProductName = ProductName;
            trailDetails.TestName = TestName;
            trailDetails.AddedOn = AddedOn;
            trailDetails.InjectionName = InjectionName;
            trailDetails.ARNo = ARNo;
            trailDetails.Operator = Operator;

            long TrailId = new DatabaseService().ManageRepeatedSequence(trailDetails);

            return TrailId;
        }

    }

    public enum ServiceState
    {
        SERVICE_STOPPED = 0x00000001,
        SERVICE_START_PENDING = 0x00000002,
        SERVICE_STOP_PENDING = 0x00000003,
        SERVICE_RUNNING = 0x00000004,
        SERVICE_CONTINUE_PENDING = 0x00000005,
        SERVICE_PAUSE_PENDING = 0x00000006,
        SERVICE_PAUSED = 0x00000007,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatus
    {
        public int dwServiceType;
        public ServiceState dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    };
}
