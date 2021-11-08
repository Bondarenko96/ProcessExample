using System;
using System.Collections.Generic;
using System.Linq;
using EleWise.ELMA.API;
using EleWise.ELMA.Model.Managers;
using Context = EleWise.ELMA.Model.Entities.ProcessContext.P_ChemicalProduction;
using EleWise.ELMA.ConfigurationModel;
using EleWise.ELMA.Documents.Managers;

namespace EleWise.ELMA.Model.Scripts
{
    public partial class P_ChemicalProduction_Scripts : EleWise.ELMA.Workflow.Scripts.ProcessScriptBase<Context>
    {
        Dictionary<TypeOfChemistry?, Func<ReestrOfMixture>> TypeOfCemistryDictionary = new Dictionary<TypeOfChemistry?, Func<ReestrOfMixture>>
        {
            { TypeOfChemistry.Catalist, () => EntityManager<ReestrOfCatalysts>.Create() },
            { TypeOfChemistry.Ceramic, () =>  EntityManager<ReestrOfCeramics>.Create() },
            { TypeOfChemistry.LiquidGlass, () => EntityManager<ReestrOfLiquidGlass>.Create() }
        };
        Dictionary<TypeOfChemistry?, string> TypeOfCemistryNameDictionary = new Dictionary<TypeOfChemistry?, string>
        {
            { TypeOfChemistry.Catalist, "CA" },
            { TypeOfChemistry.Ceramic,   "CB" },
            { TypeOfChemistry.LiquidGlass, "LG" }
        };
        Dictionary<TypeOfChemistry?, int> TypeOfChemistryHoursOfLifeDictionary = new Dictionary<TypeOfChemistry?, int>
        {
            { TypeOfChemistry.Catalist, 305 },
            { TypeOfChemistry.Ceramic,   306 },
            { TypeOfChemistry.LiquidGlass, 304 }
        };
        public virtual void AddInformationFromFirstTask(Context context)
        {
            Func<ReestrOfMixture> reestrCreator;
            TypeOfCemistryDictionary.TryGetValue(context.TypeOfMixture, out reestrCreator);
            var reestr = reestrCreator();
            reestr.MixtureWeight = context.MixtureWeight;
            reestr.Operation = OperationOfMixture.AnalysisRFA;

            context.ExpirationDate = SetTimeToExpiration(context.TypeOfMixture);

            #region Перенести таблицу исходных материалов
            foreach (var b in context.PrimaryMaterials)
            {
                #region Добавить в таблицу в реестре
                reestr.PrimaryMaterials.Add(new ReestrOfMixture_PrimaryMaterials()
                {
                    PrimaryMaterial = b.PrimaryMaterial,
                    Weight = b.Weight
                });
                #endregion

                #region Вычесть из остатка взятого материала
                b.PrimaryMaterial.CurrentBalance -= b.Weight;
                b.Save();
                #endregion
            }

            #endregion

            #region Установить номер партии
            var numerator = PublicAPI.Docflow.Objects.Nomenclature.Numerator.Load(new Guid("25711052-3305-4421-ae17-ef26f7b9a099"));
            reestr.BatchNumber = NumeratorManager.Instance.GetNewId(numerator, true).ToString();
            #endregion 

            #region Определить имя
            string prefix = "";
            TypeOfCemistryNameDictionary.TryGetValue(context.TypeOfMixture, out prefix);
            reestr.Name = prefix + DateTime.Now.ToString("ddMMyy") + "-" + reestr.BatchNumber;
            #endregion

            reestr.Save();
            context.ReestrOfMixture = reestr;
        }
        public virtual void AddInformationFromSecondTask(Context context)
        {
            context.ReestrOfMixture.ProtokolRFA = context.ProtocolRFA;
            context.ReestrOfMixture.Operation = OperationOfMixture.ReadyToUse;
        }
        /// <summary>
        /// Вернуть время списания для созданной химии
        /// </summary>
        /// <returns></returns>
        private DateTime SetTimeToExpiration(TypeOfChemistry? type)
        {
            int idOfSetting = 0;
            DateTime dateToExpiration = DateTime.Now;
            TypeOfChemistryHoursOfLifeDictionary.TryGetValue(type, out idOfSetting);
            var result = EntityManager<ProcessSettings>.Instance.Find(o => o.Id == idOfSetting).FirstOrDefault();
            if (result != null)
            {
                return dateToExpiration.AddHours(result.Value.Value);
            }
            else
            {
                throw new Exception("Не найдено настройки для выбранного типа " + type.Value);
            }

        }
        /// <summary>
        /// Если запуск процесса прошел руками, то дать выбрать какой тип химии произвести.
        /// </summary>
        /// <param name="context">Контекст процесса</param>
        /// <param name="form"></param>
        public virtual void TipHimiiOnChange(Context context, EleWise.ELMA.Model.Views.FormViewBuilder<Context> form)
        {
            if (context.TypeOfMixture != null)
            {
                form.For(o => o.TypeOfMixture).ReadOnly(true);
            }
            else
            {
                form.For(o => o.TypeOfMixture).ReadOnly(false);
            }
        }
        /// <summary>
        /// Изменить статус раствора после выхода срока годности
        /// </summary>
        /// <param name="context">Контекст процесса</param>
        /// <param name="form"></param>
        public virtual void SetExpiration(Context context)
        {
            if (context.ReestrOfMixture.Operation == OperationOfMixture.ReadyToUse)
            {
                context.ReestrOfMixture.Operation = OperationOfMixture.ExpirationDateIsOut;
                ServiceFunction.StartProcessMixtureManufacture.StartMixtureManufacture(context.ReestrOfMixture.TypeOfChemistry.Value);
            }
        }
    }
}
