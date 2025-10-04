﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ConsoleService.Model;
using Thermo.Chromeleon.Sdk.Common;
using Thermo.Chromeleon.Sdk.Interfaces.Data;
using System.Data.Entity;
using ConsoleService.Core.Dtos;

namespace ConsoleService
{
    public class DatabaseService
    {
        AIVISEntities aivisEntities;
        IEnumerable<Chrom_Intervention_Type> trailTypes;

        public DatabaseService()
        {
            aivisEntities = new AIVISEntities();
            trailTypes = GetTrailTypes();
        }

        public int ManageChromeServe(IDataVaultServer server, int UserId)
        {
            var Server = aivisEntities.Chrom_Server.Where(w => w.ServerName == server.Name).FirstOrDefault();

            if (Server == null)
            {
                Server = new Model.Chrom_Server();
                Server.ServerName = server.Name;
                Server.ServerUri = server.Url.ToString();
                Server.ImportedBy = UserId;
                Server.ImportedOn = DateTime.Now;
                Server.IsActive = true;
                aivisEntities.Entry(Server).State = EntityState.Added;
                aivisEntities.SaveChanges();
            }

            return Server.Id;

        }


        public int ManageDataVault(IDataVault dataVault, int ServeId, int UserId)
        {
            var Vault = aivisEntities.Chrom_DataVault.Where(w => w.DataVault == dataVault.Name && w.ServerId == ServeId).FirstOrDefault();

            if (Vault == null)
            {
                Vault = new Model.Chrom_DataVault();
                Vault.ServerId = ServeId;
                Vault.DataVault = dataVault.Name;
                Vault.DataVaultUri = dataVault.Url.ToString();
                Vault.ImportedBy = UserId;
                Vault.ImportedOn = DateTime.Now;
                Vault.IsActive = true;
                aivisEntities.Entry(Vault).State = EntityState.Added;
                aivisEntities.SaveChanges();
            }

            return Vault.Id;

        }

        public int ManageSequence(SequenceDetails sequenceDetails)
        {
            var Sequence = aivisEntities.Chrom_Sequence.Where(w => w.SequenceNumber == sequenceDetails.SequenceNumber && w.DataVaultId == sequenceDetails.DataVaultId).FirstOrDefault();

            if (Sequence == null)
            {
                Sequence = new Model.Chrom_Sequence();
                Sequence.DataVaultId = sequenceDetails.DataVaultId;
                Sequence.SequenceNumber = sequenceDetails.SequenceNumber;
                Sequence.SequenceUri = sequenceDetails.SequenceUri;
                Sequence.ImportedBy = sequenceDetails.ImportedBy;
                Sequence.ImportedOn = sequenceDetails.ImportedOn;
                Sequence.IsActive = sequenceDetails.IsActive;
                aivisEntities.Entry(Sequence).State = EntityState.Added;
                aivisEntities.SaveChanges();
            }

            return Sequence.Id;
        }

        public IEnumerable<Chrom_Intervention_Type> GetTrailTypes()
        {
            return aivisEntities.Chrom_Intervention_Type.ToList();
        }


        public long ManageTrails(TrailDetails trailDetails)
        {
            var Trail = aivisEntities.Chrom_Sequence_Trails.Where(w => w.Chrom_Trail_Number == trailDetails.ChromTrailNumber && trailDetails.SequenceId == trailDetails.SequenceId).FirstOrDefault();
            trailDetails.InterventionTypeId  = trailTypes.Where(w => w.Intervention == trailDetails.InterventionCode).Select(s => s.Id).FirstOrDefault();


            if (Trail == null)
            {
                Trail = new Model.Chrom_Sequence_Trails();
                Trail.SequenceId = trailDetails.SequenceId;
                Trail.InstrumentId = trailDetails.InstrumentId;
                Trail.InterventionTypeId = trailDetails.InterventionTypeId;
                Trail.InjectionName = trailDetails.InjectionName;
                Trail.Chrom_Trail_Number = trailDetails.ChromTrailNumber;
                Trail.Trails = trailDetails.Trails;
                Trail.ImportedBy = trailDetails.ImportedBy;
                Trail.ImportedOn = DateTime.Now;
                Trail.ResolveStatus = false;
                Trail.ProductName = trailDetails.ProductName;
                Trail.BatchNo = trailDetails.BatchNo;
                Trail.TestName = trailDetails.TestName;
                Trail.AddedOn = trailDetails.AddedOn;
                Trail.ARNo = trailDetails.ARNo;

                aivisEntities.Entry(Trail).State = EntityState.Added;
                aivisEntities.SaveChanges();
            }

            return Trail.Id;

        }

        public long ManageInjectionTrails(TrailDetails trailDetails)
        {
            var Trail = aivisEntities.Chrom_Sequence_Trails.Where(w => w.Chrom_Trail_Id == trailDetails.TrailId && trailDetails.SequenceId == trailDetails.SequenceId).FirstOrDefault();
            trailDetails.InterventionTypeId = trailTypes.Where(w => w.Intervention == trailDetails.InterventionCode).Select(s => s.Id).FirstOrDefault();


            if (Trail == null)
            {
                Trail = new Model.Chrom_Sequence_Trails();
                Trail.SequenceId = trailDetails.SequenceId;
                Trail.InstrumentId = trailDetails.InstrumentId;
                Trail.InterventionTypeId = trailDetails.InterventionTypeId;
                Trail.InjectionName = trailDetails.InjectionName;
                Trail.Chrom_Trail_Id = trailDetails.TrailId;
                Trail.Trails = trailDetails.Trails;
                Trail.ImportedBy = trailDetails.ImportedBy;
                Trail.ImportedOn = DateTime.Now;
                Trail.ResolveStatus = false;
                Trail.ProductName = trailDetails.ProductName;
                Trail.BatchNo = trailDetails.BatchNo;
                Trail.TestName = trailDetails.TestName;
                Trail.AddedOn = trailDetails.AddedOn;
                Trail.ARNo = trailDetails.ARNo;

                aivisEntities.Entry(Trail).State = EntityState.Added;
                aivisEntities.SaveChanges();
            }

            return Trail.Id;

        }

        public bool DeleteSubmittedReviewedTrail(int SequenceId, string TrailType, string DeletedRemarks)
        {
            var NotSubmitted = (
                                    from trail in aivisEntities.Chrom_Sequence_Trails
                                    join type in aivisEntities.Chrom_Intervention_Type on trail.InterventionTypeId equals type.Id
                                    where trail.SequenceId == SequenceId && type.Intervention == TrailType
                                    select trail
                ).ToList();

            if (NotSubmitted.Any())
            {
                foreach (var item in NotSubmitted)
                {
                    item.IsDeleted = true;
                    item.DeletedRemarks = DeletedRemarks;
                    aivisEntities.Entry(item).State = EntityState.Modified;
                }

                aivisEntities.SaveChanges();
            }

            return true;
        }

        public long ManageRepeatedSequence(TrailDetails trailDetails)
        {
            var ExistingSequences = aivisEntities.Chrom_Sequence_Trails.Where(w => w.ARNo == trailDetails.ARNo && w.TestName == trailDetails.TestName).ToList();
            if (ExistingSequences.Any())
            {
                trailDetails.InterventionTypeId = trailTypes.Where(w => w.Intervention == trailDetails.InterventionCode).Select(s => s.Id).FirstOrDefault();
                List<int> newSequenceIds = new List<int>();

                if (ExistingSequences.Where(w => w.SequenceId == trailDetails.SequenceId).Count() == 0)
                {
                    //enter existing if repeated
                    foreach (var item in ExistingSequences)
                    {
                        if (!newSequenceIds.Contains(item.SequenceId))
                        {
                            var Trail = new Model.Chrom_Sequence_Trails();
                            Trail.SequenceId = item.SequenceId;
                            Trail.InstrumentId = item.InstrumentId;
                            Trail.InterventionTypeId = trailDetails.InterventionTypeId;
                            Trail.InjectionName = item.InjectionName;
                            Trail.Chrom_Trail_Id = item.Chrom_Trail_Id;
                            Trail.Trails = item.Trails;
                            Trail.ImportedBy = item.ImportedBy;
                            Trail.ImportedOn = item.ImportedOn;
                            Trail.ResolveStatus = false;
                            Trail.ProductName = item.ProductName;
                            Trail.BatchNo = item.BatchNo;
                            Trail.TestName = item.TestName;
                            Trail.AddedOn = item.AddedOn;
                            Trail.ARNo = item.ARNo;

                            aivisEntities.Entry(Trail).State = EntityState.Added;

                            newSequenceIds.Add(item.SequenceId);
                        }
                    }

                    //new rails
                    var TrailNew = new Model.Chrom_Sequence_Trails();
                    TrailNew.SequenceId = trailDetails.SequenceId;
                    TrailNew.InstrumentId = trailDetails.InstrumentId;
                    TrailNew.InterventionTypeId = trailDetails.InterventionTypeId;
                    TrailNew.InjectionName = trailDetails.InjectionName;
                    TrailNew.Chrom_Trail_Id = trailDetails.TrailId;
                    TrailNew.Trails = trailDetails.Trails;
                    TrailNew.ImportedBy = trailDetails.ImportedBy;
                    TrailNew.ImportedOn = DateTime.Now;
                    TrailNew.ResolveStatus = false;
                    TrailNew.ProductName = trailDetails.ProductName;
                    TrailNew.BatchNo = trailDetails.BatchNo;
                    TrailNew.TestName = trailDetails.TestName;
                    TrailNew.AddedOn = trailDetails.AddedOn;
                    TrailNew.ARNo = trailDetails.ARNo;

                    aivisEntities.Entry(TrailNew).State = EntityState.Added;

                    aivisEntities.SaveChanges();

                    return TrailNew.Id;
                }
            }

            return trailDetails.Id;
        }

        


    }
}
