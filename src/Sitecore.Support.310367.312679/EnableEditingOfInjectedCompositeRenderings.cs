



namespace Sitecore.Support.XA.Feature.Composites.Pipelines.GetChromeData
{
    // EnableEditingOfInjectedCompositeRenderings
    using Microsoft.Extensions.DependencyInjection;
    using Sitecore.Data;
    using Sitecore.DependencyInjection;
    using Sitecore.Diagnostics;
    using Sitecore.Layouts;
    using Sitecore.Pipelines.GetChromeData;
    using Sitecore.XA.Feature.Composites;
    using Sitecore.XA.Feature.Composites.Extensions;
    using Sitecore.XA.Feature.Composites.Pipelines.GetChromeData;
    using Sitecore.XA.Feature.Composites.Services;
    using Sitecore.XA.Foundation.Abstractions;
    using Sitecore.XA.Foundation.Caching;
    using Sitecore.XA.Foundation.Abstractions.Configuration;
    using Sitecore.XA.Foundation.Presentation.Layout;
    using Sitecore.XA.Foundation.Editing;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using Sitecore.XA.Foundation.PlaceholderSettings.Services;
    using System.Web;
    using Sitecore.Data.Fields;
    using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;

    public class EnableEditingOfInjectedCompositeRenderings : EnableEditingOnCompositeItems
    {
        public EnableEditingOfInjectedCompositeRenderings(ILayoutsPageContext layoutsPageContext)
            : this(layoutsPageContext, new Sitecore.XA.Foundation.Abstractions.Context())
        {
        }

        public EnableEditingOfInjectedCompositeRenderings(ILayoutsPageContext layoutsPageContext, IContext context)
        {
            LayoutsPageContext = layoutsPageContext;
            Context = context;
            EditingConfiguration configuration = ServiceLocator.ServiceProvider.GetService<IConfiguration<EditingConfiguration>>().GetConfiguration();
            DictionaryCacheInstance = new DictionaryCache("SXA[PlaceholderChromeData]", configuration.PlaceholderChromeDataCacheMaxSize);
        }

        protected static DictionaryCache DictionaryCacheInstance
        {
            get;
            private set;
        }
        protected ILayoutsPageContext LayoutsPageContext
        {
            get;
        }
        protected DictionaryCache DictionaryCache
        {
            get
            {
                return DictionaryCacheInstance;
            }
        }
        protected IContext Context
        {
            get;
        }
        public override void Process(GetChromeDataArgs args)
        {
            if (ServiceLocator.ServiceProvider.GetService<IConfiguration<CompositesConfiguration>>().GetConfiguration().OnPageEditingEnabled)
            {
                Assert.ArgumentNotNull(args, "args");

                if (args.ChromeData.Custom.ContainsKey("editable") && !IsFxmContext(args) && (IsRenderingFromComposite(args) || IsPlaceholderFromComposite(args)) && !IsFromPartialDesign(args))
                {
                    LayoutModel layoutModel = GetLayoutModel();

                    string phKey;
                    if (layoutModel != null)
                    {
                        List<RenderingModel> renderingsCollection = layoutModel.Devices[ServiceLocator.ServiceProvider.GetService<IContext>().Device.ID.ToString()].Renderings.RenderingsCollection;
                        List<ID> duplicatedRenderingsUniqueIds = renderingsCollection.GetDuplicatedRenderingsUniqueIDs().ToList();
                        RenderingReference renderingReference;
                        if ((renderingReference = (args.CustomData["renderingReference"] as RenderingReference)) != null && IsDuplicated(duplicatedRenderingsUniqueIds, new ID(renderingReference.UniqueId)))
                        {
                            return;
                        }
                        if ((phKey = (args.CustomData["placeholderKey"] as string)) != null)
                        {
                            foreach (RenderingModel item in from m in renderingsCollection
                                                            where m.Placeholder != null
                                                            where m.Placeholder.Equals(phKey)
                                                            select m)
                            {
                                if (IsDuplicated(duplicatedRenderingsUniqueIds, item.UniqueId))
                                {
                                    return;
                                }
                            }
                        }
                    }

                    DictionaryCacheValue dictionaryCacheValue = null;

                    if (Context?.Device != null)
                    {
                        string id = CacheKey(args);
                        dictionaryCacheValue = DictionaryCache.Get(id);
                    }

                    bool valc = false;
                    if ((phKey = (args.CustomData["placeholderKey"] as string)) != null)
                    {
                        valc = ValidateCommands(dictionaryCacheValue, phKey);
                    }

                    if (dictionaryCacheValue != null && dictionaryCacheValue.Properties.Keys.Count > 0 && valc)
                    {
                        args.ChromeData.Custom["editable"] =
                            dictionaryCacheValue.Properties
                                ["editable"]; // Sitecore.Support.310367.312679, loads editable from cache instead of hardcoding
                    }
                    else
                    {
                        args.ChromeData.Custom["editable"] = true.ToString().ToLowerInvariant();// Original
                    }

                    Log.Info("customcache :" + CacheKey(args) + "   >   " + args.ChromeData.Custom["editable"], "");
                }
            }
        }
        protected virtual bool ValidateCommands(DictionaryCacheValue cacheValue, string placeholderKey)
        {
            List<WebEditButton> list;
            if ((list = (cacheValue.Properties["Commands"] as List<WebEditButton>)) != null && list.Count > 0)
            {
                List<WebEditButton> source = (from b in list
                                              where b.Click.Contains("referenceId=")
                                              select b).ToList();
                if (source.Any() && HttpContext.Current.Request.Form.AllKeys.Contains("layout"))
                {
                    LayoutModel layoutModel = GetLayoutModel();
                    DeviceModel currentDeviceModel = GetCurrentDeviceModel(layoutModel);
                    Placeholder placeholder = new Placeholder(placeholderKey);
                    RenderingModel renderingModel = (from r in currentDeviceModel.Renderings.RenderingsCollection
                                                     where new Placeholder(r.Placeholder).IsPartOf(placeholder)
                                                     orderby r.Placeholder.Length descending
                                                     select r).FirstOrDefault();
                    string referenceId = (renderingModel != null) ? renderingModel.UniqueId.ToSearchID().ToUpperInvariant() : null;
                    return source.Any((WebEditButton b) => b.Click.Contains("referenceId=" + referenceId));
                }
            }
            return true;
        }
        protected virtual DeviceModel GetCurrentDeviceModel(LayoutModel model)
        {
            ID iD = Sitecore.Context.Device.ID;
            return model.Devices[iD.ToString()];
        }
        protected string CacheKey(GetChromeDataArgs args)
        {
            string text = args.CustomData["placeHolderKey"] as string;
            return string.Format("SXA::GetPlaceholderChromeData::{0}:{1}:{2}:{3}", Context.Device.ID, text, args.Item.ID, args.ChromeType);
        }

        protected virtual bool IsDuplicated(IEnumerable<ID> duplicatedRenderingsUniqueIds, ID uniqueId)
        {
            return duplicatedRenderingsUniqueIds.Any((ID id) => id == uniqueId);
        }

        protected virtual LayoutModel GetLayoutModel()
        {
            XElement xmlLayoutDefinition = ServiceLocator.ServiceProvider.GetService<IOnPageEditingContextService>().XmlLayoutDefinition;
            if (xmlLayoutDefinition != null)
            {
                return new LayoutModel(xmlLayoutDefinition.ToString());
            }
            return null;
        }
        protected bool SxaRenderingSourcesKeysExists()
        {
            return HttpContext.Current.Items["SXA-RENDERING-SOURCES"] != null;
        }
    }
}