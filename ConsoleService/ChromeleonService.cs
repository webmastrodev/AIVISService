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

namespace ConsoleService
{
    public class ChromeleonService
    {
        int ServiceUserId = int.Parse(ConfigurationManager.AppSettings["ServiceUser"]);
        string SequenceNotSubmittedTrailType = ConfigurationManager.AppSettings["SequenceNotSubmittedTrailType"];
        string SequenceNotReviewedTrailType = ConfigurationManager.AppSettings["SequenceNotReviewedTrailType"];

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
                    ProcessSequence(folder.Url, DataVaultId);
                }

                ProcessSequence(datavault.Url, DataVaultId);
            }
        }

        private void ProcessSequence(Uri uri, int DataVaultId)
        {
            var itemFactory =CmSdk.GetItemFactory();

            //get Child data, ex: sequence
            if (itemFactory.TryGetItem(uri, out IDataItem dataItem))
            {
                IParentItem parentItem = dataItem as IParentItem;
                if (parentItem != null)
                {
                    foreach (var child in parentItem.Children)
                    {
                        var parentChildItem = child as IParentItem;

                        if (parentChildItem is ISequence)
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


                            GetSequenceCustomInfo(parentChildItem as ISequence, out string SeqBatchNo, out string SeqProductName, out string SeqTestName, out string ARNo);

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
                                    ManageTrails(SequenceId, atril.Number, atril.Description, "DeletedInjection", SeqBatchNo, SeqProductName, SeqTestName, atril.TransactionLogEntry.StartTime.LocalDateTime, ARNo);
                                }
                                else if (atril.ItemType.ToString() == "IChromatogram")
                                {

                                    // Manual Integration
                                    ManageTrails(SequenceId, atril.Number, atril.Description, "ManualIntegration", SeqBatchNo, SeqProductName, SeqTestName, atril.TransactionLogEntry.StartTime.LocalDateTime, ARNo);
                                }
                            }

                            ////for Changed trail
                            //if (parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Changed" && w.ItemType.Name == "IProcessingMethod").Count() > 0)
                            //{
                            //    HandleChangedTrails(SequenceId, parentChildItem as ISequence);
                            //}

                            ////Check deleted Injection
                            //ISequence currentSequence = (parentChildItem as ISequence);
                            //foreach (var field in currentSequence.CustomFields)
                            //{
                            //    Console.WriteLine(field.ToString());
                            //}


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
                                        ManageTrails(SequenceId, chTrail.Number, chTrail.Description, "ChangedInjection", SeqBatchNo, SeqProductName, SeqTestName, chTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo);
                                    }
                                }

                                var RenamedInjectionsTrails = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString().ToLower().Contains("ren") && w.ItemType.Name == "ISequence" && w.Number > LastFinishRunTrail.Number).ToList();
                                if (RenamedInjectionsTrails.Any())
                                {
                                    foreach (var renTrail in RenamedInjectionsTrails)
                                    {
                                        //Rename Injection
                                        ManageTrails(SequenceId, renTrail.Number, renTrail.Description, "RenamedInjection", SeqBatchNo, SeqProductName, SeqTestName, renTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo);
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
                                ManageTrails(SequenceId, 0, string.Empty, "NotSubmitted", SeqBatchNo, SeqProductName, SeqTestName, DateTime.Now, ARNo);
                            }

                            // check reviewed trails exists
                            var FirstReviewedTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Reviewed").OrderBy(o => o.Number).FirstOrDefault();
                            if (FirstReviewedTrail == null)
                            {
                                // Not Reviwed
                                // make Trail Number 0
                                ManageTrails(SequenceId, 0, string.Empty, "NotReviewed", SeqBatchNo, SeqProductName, SeqTestName, DateTime.Now, ARNo);
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
                                    ManageTrails(SequenceId, SignatureRemovedTrail.Number, SignatureRemovedTrail.Description, "SignatureRemoved", SeqBatchNo, SeqProductName, SeqTestName, SignatureRemovedTrail.TransactionLogEntry.StartTime.LocalDateTime, ARNo);
                                }
                            }

                            #endregion

                            #region repeat batch Sequence
                            //if (ARNo != "NA" && SeqTestName != "NA")
                            if (SeqTestName != "NA")
                            {
                                ManageRepeatSequenceTrails(SequenceId, 0, string.Empty, "RepeatSequence", SeqBatchNo, SeqProductName, SeqTestName, DateTime.Now, ARNo);
                            }

                            #endregion
                        }
                    }
                }
            }
        }


        void HandleChangedTrails(int SequenceId, ISequence seq)
        {
            var injectionAccess = CmSdk.GetInstrumentAccess();
            foreach (var inject in seq.Injections)
            {
                IProcessingMethod processMethod = inject.ProcessingMethod;
                foreach (var component in processMethod.Components)
                {
                    foreach (var concentrationLevel in component.ConcentrationLevels)
                    {

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
                        if (intrinject.AuditTrail.Messages.Where(w => w.Message.Contains("Instrument method aborted")).Any())
                        {
                            GetInjectionCustomInfo(intrinject, out string BatchNo, out string ProductName, out string TestName, out string ARNo);
                            ManageInjectionTrails(SequenceId, intrinject.AuditTrail.Id, intrinject.Comment.ToString(), "AbortedRunInstrument", BatchNo, ProductName, TestName, intrinject.LastUpdate.StartTime.LocalDateTime, ARNo, intrinject.Name);
                        }

                        if (intrinject.AuditTrail.Messages.Where(w => w.Message.Contains("Stopping the sequence queue (Immediately)")).Any())
                        {
                            GetInjectionCustomInfo(intrinject, out string BatchNo, out string ProductName, out string TestName, out string ARNo);
                            ManageInjectionTrails(SequenceId, intrinject.AuditTrail.Id, intrinject.Comment.ToString(), "AbortedRun", BatchNo, ProductName, TestName, intrinject.LastUpdate.StartTime.LocalDateTime, ARNo, intrinject.Name);
                        }

                    }

                }
            }
        }

        void GetInjectionCustomInfo(IInjection Inj, out string BatchNo, out string ProductName, out string TestName, out string ARNo)
        {
            BatchNo = Inj.CustomFields["BATCH_NO"] == null ? null : Inj.CustomFields["BATCH_NO"].ToString();
            ProductName = Inj.CustomFields["PRODUCT_NAME"] == null ? null : Inj.CustomFields["PRODUCT_NAME"].ToString();
            TestName = Inj.CustomFields["TEST"] == null ? null : Inj.CustomFields["TEST"].ToString();
            ARNo = Inj.CustomFields["AR_NO"] == null ? null : Inj.CustomFields["AR_NO"].ToString();
        }

        void GetSequenceCustomInfo(ISequence Seq, out string BatchNo, out string ProductName, out string TestName, out string ARNo)
        {
            var Injections = Seq.Injections;
            ProductName = null;
            BatchNo = null;
            TestName = null;
            ARNo = null;

            if (Injections.Any() && Injections.CustomFieldDefinitions.Count > 0)
            {
                BatchNo = (Injections.Where(w => w.CustomFields["BATCH_NO"] != null).Select(s => s.CustomFields["BATCH_NO"]).FirstOrDefault()).ToString();

                if (Injections.Where(w => w.CustomFields["PRODUCT_NAME"] != null).Select(s => s.CustomFields["PRODUCT_NAME"]).FirstOrDefault() != null)
                {
                    ProductName = (Injections.Where(w => w.CustomFields["PRODUCT_NAME"] != null).Select(s => s.CustomFields["PRODUCT_NAME"]).FirstOrDefault()).ToString();
                }

                TestName = (Injections.Where(w => w.CustomFields["TEST"] != null).Select(s => s.CustomFields["TEST"]).FirstOrDefault()).ToString();

                ARNo = (Injections.Where(w => w.CustomFields["AR_NO"] != null).Select(s => s.CustomFields["AR_NO"]).FirstOrDefault()).ToString();
            }

            
        }

        long ManageTrails(int SequenceId, int TrailNumber, string Description, string InterventionCode, string BatchNo, string ProductName, string TestName, DateTime AddedOn, string ARNo, string InjectionName = null)
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

            long TrailId = new DatabaseService().ManageTrails(trailDetails);

            return TrailId;
        }

        long ManageInjectionTrails(int SequenceId, Guid AuditTrailId, string Description, string InterventionCode, string BatchNo, string ProductName, string TestName, DateTime AddedOn, string ARNo, string InjectionName = null)
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


            long TrailId = new DatabaseService().ManageInjectionTrails(trailDetails);

            return TrailId;
        }

        long ManageRepeatSequenceTrails(int SequenceId, int TrailNumber, string Description, string InterventionCode, string BatchNo, string ProductName, string TestName, DateTime AddedOn, string ARNo, string InjectionName = null)
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

            long TrailId = new DatabaseService().ManageRepeatedSequence(trailDetails);

            return TrailId;
        }


    }
}
