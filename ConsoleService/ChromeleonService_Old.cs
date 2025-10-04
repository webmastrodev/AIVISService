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
    public class ChromeleonService_Old
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
         * 
         * 
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

                            ProcessSequence(datavault.Url, DataVaultId);
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

        private async void ProcessSequence(Uri uri, int DataVaultId)
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
                        //audit trail
                        //foreach (var atril in child.ItemAuditTrail)
                        //{
                        //    Console.WriteLine("Code: " + atril.Operation.ToString() + " Name: " + atril.ItemType.FullName + "   Audit Trail: " + atril.Description);
                        //}

                        var parentChildItem = child as IParentItem;




                        if (parentChildItem is ISequence && parentChildItem.Name == "QC1066DULO_AS_0205A")
                        {
                            //Uncomment this
                            //Console.WriteLine("Child Name : " + child.Name + "   Child URL: " + child.Url + "  Type: " + child.GetType());

                            GetSequenceCustomInfo(parentChildItem as ISequence, out string BatchNo, out string ProductName, out string TestName);

                            SequenceDetails sequenceDetails = new SequenceDetails();
                            sequenceDetails.DataVaultId = DataVaultId;
                            sequenceDetails.SequenceNumber = parentChildItem.Name;
                            sequenceDetails.SequenceUri = parentChildItem.Url.ToString();
                            sequenceDetails.ImportedBy = ServiceUserId;
                            sequenceDetails.ImportedOn = DateTime.Now;
                            sequenceDetails.IsActive = true;

                            int SequenceId = new DatabaseService().ManageSequence(sequenceDetails);





                            #region Old Working Part, don't change

                            foreach (var atril in parentChildItem.ItemAuditTrail)
                            {



                                //    //Uncomment this
                                //    if (atril.Operation.ToString() == "AbortedRun" 
                                //        || (atril.Operation.ToString() == "Renamed" && atril.ItemType.Name == "IInjection") 
                                //        || atril.ItemType.ToString() == "IChromatogram"
                                //    )
                                //    {


                                //        TrailDetails trailDetails = new TrailDetails();
                                //        trailDetails.SequenceId = SequenceId;
                                //        trailDetails.ChromTrailNumber = atril.Number;
                                //        trailDetails.Trails = atril.Description;
                                //        trailDetails.ImportedBy = ServiceUserId;
                                //        if (atril.ItemType.ToString() == "IChromatogram")
                                //        {
                                //            trailDetails.InterventionCode = "ManualIntegration";
                                //        }
                                //        else
                                //        {
                                //            trailDetails.InterventionCode = atril.Operation.ToString();
                                //        }

                                //        long TrailId = new DatabaseService().ManageTrails(trailDetails);

                                //        //Console.WriteLine("1 Trail Created With Number : " + atril.Number);

                                //        //Console.WriteLine("Code: " + atril.Operation.ToString() + " Number: " + atril.Number + " Name: " + atril.ItemType.FullName + "   Audit Trail: " + atril.Description);
                                //    }
                                //else if (atril.Operation.ToString().ToLower().Contains("change") && atril.ItemType.Name == "ISequence")
                                //  {
                                //      //Changed INjection
                                //      Console.WriteLine("Operation: " + atril.Operation + " Type: " + atril.ItemType);
                                //  }
                                //    else if(atril.ItemType.Name.Contains("Chroma"))
                                //    {
                                //        Console.WriteLine("Code: " + atril.Operation.ToString() + " Name: " + atril.ItemType.Name + "   Audit Trail: " + atril.Description);
                                //    }


                                //Console.WriteLine("Operation: " + atril.Operation);

                                if (atril.Operation.ToString() == "AbortedRun")
                                {
                                    HandleAbortedRunInjection(SequenceId, parentChildItem as ISequence);
                                    //Console.WriteLine("Operation: " + atril.Operation + " Type: " + atril.ItemType + " Description: " + atril.Description);
                                }


                            }

                            #endregion



                            #region New Code Working


                            foreach (var atril in parentChildItem.ItemAuditTrail)
                            {
                                if (atril.Operation.ToString() == "Deleted" && atril.ItemType.Name == "IInjection") //add condition of "Raw Data Contained"
                                {
                                    // Deleted Injection
                                }
                                else if (atril.ItemType.ToString() == "IChromatogram")
                                {
                                    // Manual Integration
                                }

                            }

                            //Check Change Injection
                            var LastFinishRunTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "FinishedRun").OrderByDescending(o => o.Number).FirstOrDefault();
                            if (LastFinishRunTrail != null)
                            {
                                var ChangedInjections = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Changed" && w.ItemType.Name == "ISequence" && w.Number > LastFinishRunTrail.Number).ToList();
                                if (ChangedInjections.Any())
                                {
                                    foreach (var inject in ChangedInjections)
                                    {
                                        //Change Injection
                                    }
                                }
                            }


                            // check Submitted Trails Exists
                            var LastSubmitTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Submitted").OrderByDescending(o => o.Number).FirstOrDefault();

                            if (LastSubmitTrail != null)
                            {
                                // mark delete any not Submitted Trails found for Sequence
                                new DatabaseService().DeleteSubmittedReviewedTrail(SequenceId, SequenceNotSubmittedTrailType, Constants.DeleteSubmittedTrailMsg);

                                // check reviewed trails exists
                                var LastReviewedTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "Reviewed" && w.Number > LastSubmitTrail.Number).FirstOrDefault();

                                if (LastReviewedTrail == null)
                                {
                                    // Not Reviwed
                                    // make Trail Number 0
                                }
                                else
                                {
                                    // mark delete any not Submitted Trails found for Sequence
                                    new DatabaseService().DeleteSubmittedReviewedTrail(SequenceId, SequenceNotReviewedTrailType, Constants.DeleteReviewedTrailMsg);

                                    // if signature removed found after reviewed
                                    var SignatureRemovedTrail = parentChildItem.ItemAuditTrail.Where(w => w.Operation.ToString() == "SignatureRemoved" && w.Number > LastReviewedTrail.Number).FirstOrDefault();
                                    if (SignatureRemovedTrail != null)
                                    {
                                        //signature Removed
                                    }
                                }
                            }
                            else
                            {
                                //Not Submitted
                                // make Trail Number 0
                            }


                            #endregion



                            //foreach (var atril in parentChildItem.ItemAuditTrail)
                            //{
                            //    //Console.WriteLine(atril.TransactionLogEntry);
                            //    //for changed injection
                            //    if (atril.Operation.ToString().ToLower().Contains("change") && atril.ItemType.Name == "ISequence")
                            //    {
                            //        Console.WriteLine("Operation: " + atril.Operation + " Type: " + atril.ItemType);
                            //    }



                            //    //if (atril.Operation.ToString().ToLower().Contains("change"))
                            //    //{
                            //    //    Console.WriteLine("Code: " + atril.Operation.ToString() + " Number: " + atril.Number + " Name: " + atril.ItemType.FullName);
                            //    //}

                            //}


                            //var Injections = (parentChildItem as ISequence).Injections;
                            //foreach (var inject in Injections)
                            //{

                            //    //Console.WriteLine(inject.Id +  "Inject Name: " +  inject.Name + " -  INS Method: " + inject.InstrumentMethodName);

                            //    if (inject.AuditTrail != null)
                            //    {
                            //        foreach (var atril in inject.ItemAuditTrail)
                            //        {
                            //            //if (atril.Operation.ToString() == "AbortedRun")
                            //            //{
                            //            //    Console.WriteLine("Injections Code: " + atril.Operation.ToString() + " Name: " + atril.ItemType.FullName + "   Audit Trail: " + atril.Description);
                            //            //}

                            //            //Console.WriteLine("Code: " + atril.Operation.ToString() + atril + " Number: " + atril.Number + " Name: " + atril.ItemType.FullName + "   Audit Trail: " + atril.Description);
                            //            Console.WriteLine("Code: " + atril.Operation.ToString());

                            //        }
                            //    }
                            //}


                            //IInstrument selectedInstrument = GetInstrument(parentChildItem as ISequence);
                            //if (selectedInstrument != null)
                            //{
                            //    if (await ConnectToInstrumentAsync(selectedInstrument))
                            //    {
                            //        ReadOnlineAuditTrails(selectedInstrument);
                            //    }
                            //}




                            //Console.WriteLine("Has Child Children : " + parentChildItem != null ? parentChildItem.Children.Any() : false);





                            //Console.WriteLine("Checking Instrument.....");

                            //ISequence seq = parentChildItem as ISequence;

                            //IInstrument selectedInstrument = GetInstrument(seq);

                            //if (selectedInstrument != null)
                            //{
                            //    if (await ConnectToInstrumentAsync(selectedInstrument))
                            //    {
                            //        ReadOnlineAuditTrails(selectedInstrument);
                            //    }
                            //}
                        }

                    }


                }
            }
        }

        //IInstrument GetInstrument(ISequence seq)
        //{
        //    var instrumentAccess = CmSdk.GetInstrumentAccess();
        //    IInstrument instrument = null;
        //    if (seq != null)
        //    {
        //        if (seq.InstrumentUri != null)
        //        {
        //            instrumentAccess.TryFindInstrument(seq.InstrumentUri, out instrument);
        //        }
        //    }

        //    return instrument;
        //}

        //async Task<bool> ConnectToInstrumentAsync(IInstrument instrument)
        //{
        //    var result = instrument.BeginConnect(null, null);
        //    instrument.EndConnect(result);

        //    //connect and return IInstrument object
        //    //Progress result = await instrument.ConnectAsync();
        //    //return result == Progress.Completed;
        //    return await Task.FromResult(true);
        //}

        //void ReadOnlineAuditTrails(IInstrument instrument)
        //{
        //    StringBuilder sb = new StringBuilder();

        //    foreach (var auditTrailMessage in instrument.OnlineAuditTrail)
        //    {
        //        sb.AppendFormat("[{0}]", auditTrailMessage.Category);
        //        sb.AppendLine(auditTrailMessage.Message);
        //    }

        //    Console.WriteLine(sb.ToString());
        //}

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
                        foreach (var trail in intrinject.ItemAuditTrail)
                        {
                            if (trail.Operation.ToString() == "AbortedRun" )
                            {
                                if (trail.Description.ToLower().Contains("instrument method aborted"))
                                {
                                    // Aborted Run Instrumental 
                                }
                                else if (trail.Description.ToLower().Contains("stopping the sequence queue"))
                                { 
                                    // Aborted Run manual
                                }
                            }
                        }

                    }

                }
            }
        }


        void GetSequenceCustomInfo(ISequence sec, out string BatchNo, out string ProductName, out string TestName)
        {
            BatchNo = string.Empty;
            ProductName = string.Empty;
            TestName = string.Empty;
            var Inject = sec.Injections[0];
            BatchNo = sec.CustomFields["BATCH_NO"] == null ? Inject.CustomFields["BATCH_NO"].ToString() : sec.CustomFields["BATCH_NO"].ToString();
            ProductName = Inject.CustomFields["PRODUCT_NAME"].ToString();
            TestName = Inject.CustomFields["TEST"].ToString();
        }


    }
}
