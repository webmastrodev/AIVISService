using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Thermo.Chromeleon.Sdk.Common;
using Thermo.Chromeleon.Sdk.Interfaces.Data;
using Thermo.Chromeleon.Sdk.Interfaces.Instruments;
using Thermo.Chromeleon.Sdk.Utilities;
using Thermo.Chromeleon.Sdk.Interfaces;
using Thermo.Chromeleon.Sdk.UserManagement;
using System.Configuration;
using Thermo.Chromeleon.Sdk.Interfaces.Data.Collections;
using ConsoleService.Core.Dtos;
using ConsoleService.Core;
using Thermo.Chromeleon.Sdk.Interfaces.Types;


namespace ConsoleService
{
    public class ChromeleonService
    {
        int ServiceUserId = int.Parse(ConfigurationManager.AppSettings["ServiceUser"]);
        string SequenceNotSubmittedTrailType = ConfigurationManager.AppSettings["SequenceNotSubmittedTrailType"];
        string SequenceNotReviewedTrailType = ConfigurationManager.AppSettings["SequenceNotReviewedTrailType"];
        string SiteName = ConfigurationManager.AppSettings["SiteName"];


        /* NOT FOUND FIELD
         * Batch No
         * Product/Material
         * Actual Audit Trail Created Time
         * Commnet Field
         * Additional Info Field
         * Injection Name from Sequence Audit Trails Item Type IInjection
         * * Need Moved Trails Type Data
         * * Aborted Run data in Chromeleon
         * * Renamed injection not working
         * 
        */


        public void ProcessBackService()
        {
            //define scope
            using (var scope = new CmSdkScope())
            {
                //Ligon
                CmSdk.Logon.DoLogon();

                //check if login success ?
                if (CmSdk.Logon.IsLoggedOn)
                {
                    Console.WriteLine("Chomeleon Logged In Done...");

                    var itemFactory = CmSdk.GetItemFactory();

                    foreach (var server in itemFactory.DataVaultServers)
                    {
                        int ServerId = new DatabaseService().ManageChromeServe(server, ServiceUserId);

                        //get server
                        Console.WriteLine("Server: " + server.Name + "   Server URL: " + server.Url);

                        //get server and data vault
                        foreach (var datavault in server.DataVaults)
                        {
                            int DataVaultId = new DatabaseService().ManageDataVault(datavault, ServerId, ServiceUserId);
                            Console.WriteLine("Data Vault: " + datavault.Name + "   Data Vault URL: " + datavault.Url);

                            ProcessDataVault(datavault, DataVaultId, itemFactory);
                            //ProcessSequence(datavault.Url, DataVaultId);
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

        private void ProcessDataVault(IDataVault datavault, int DataVaultId, IItemFactory itemFactory)
        {
            if (datavault != null)
            {
                var Folders = datavault.Children.Where(w => w.GetType().Name == "Folder").ToList();

                foreach (var folder in Folders)
                {
                    Console.WriteLine(folder.Name);

                    if (folder.Name != "$RecycleBin$")
                    {
                        foreach (var item in folder.ItemAuditTrail)
                        {
                            if (item.Operation.ToString() == "Moved" && item.ItemType.Name == "ISequence")
                            {
                                Console.WriteLine("Inside Deleted Folder");

                                var TestItem = item.RelativePath;
                                //var TestSeq = item.RelativePath as ISequence;

                                var DeleteFolder = Folders.Where(w => w.Name == "$RecycleBin$").FirstOrDefault();

                                

                                //if (DeleteFolder != null)
                                //{
                                //    DeleteFolder.
                                //}
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

                        if (parentChildItem is ISequence) // && child.Name == "QC330OMEP_ASRS_3007A - Copy"
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
                                else if (atril.Operation.ToString() == "Changed" && atril.ItemType.Name == "IProcessingMethod")
                                {
                                    var Injections = (parentChildItem as ISequence).Injections;

                                    if (Injections.Any())
                                    {
                                        IProcessingMethod processingMethod = Injections[0].ProcessingMethod;
                                        foreach (var component in processingMethod.Components)
                                        {
                                            if (component.CustomFields.Where(w => w.Definition.Name.StartsWith("STD") && w.Definition.Name.EndsWith("WT")).Count() > 0)
                                            {
                                                ManageTrails(SequenceId, atril.Number, atril.Description, "Changed", SeqBatchNo, SeqProductName, SeqTestName, atril.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, atril.TransactionLogEntry.User.Name);
                                            }
                                        }

                                    }
                                }



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


                            var LastFinishRunTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "FinishedRun").OrderByDescending(o => o.Number).FirstOrDefault();
                            if (LastFinishRunTrail != null)
                            {
                                //Check Change Injection
                                var ChangedInjectionsTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Changed" && w.ItemType.Name == "ISequence" && w.Number > LastFinishRunTrail.Number).ToList();
                                if (ChangedInjectionsTrails.Any())
                                {
                                    foreach (var chTrail in ChangedInjectionsTrails)
                                    {
                                        //Change Injection
                                        ManageTrails(SequenceId, chTrail.Number, chTrail.Description, "ChangedInjection", SeqBatchNo, SeqProductName, SeqTestName, chTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, chTrail.TransactionLogEntry.User.Name);
                                    }
                                }

                                var RenamedInjectionsTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString().ToLower().Contains("ren") && w.ItemType.Name == "ISequence" && w.Number > LastFinishRunTrail.Number).ToList();
                                if (RenamedInjectionsTrails.Any())
                                {
                                    foreach (var renTrail in RenamedInjectionsTrails)
                                    {
                                        //Rename Injection
                                        ManageTrails(SequenceId, renTrail.Number, renTrail.Description, "RenamedInjection", SeqBatchNo, SeqProductName, SeqTestName, renTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo, InstrumentId, renTrail.TransactionLogEntry.User.Name);
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
}
