namespace Sitecore.Support.XA.Feature.Composites.Pipelines.GetChromeData
{
  // EnableEditingOfInjectedCompositeRenderings
  using Microsoft.Extensions.DependencyInjection;
  using Sitecore.Configuration;
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
  using Sitecore.XA.Foundation.Presentation.Layout;
  using System.Collections.Generic;
  using System.Linq;
  using System.Xml.Linq;

  public class EnableEditingOfInjectedCompositeRenderings : EnableEditingOnCompositeItems
  {
    protected static DictionaryCache DictionaryCacheInstance
    {
      get;
    } = new DictionaryCache("SXA[PlaceholderChromeData]", StringUtil.ParseSizeString(Settings.GetSetting("XA.Foundation.Editing.PlaceholderChromeDataCacheMaxSize", "50MB")));


    protected DictionaryCache DictionaryCache
    {
      get
      {
        return DictionaryCacheInstance;
      }
    }
    public override void Process(GetChromeDataArgs args)
    {
      if (ServiceLocator.ServiceProvider.GetService<ICompositesConfiguration>().OnPageEditingEnabled)
      {
        Assert.ArgumentNotNull(args, "args");
        if (args.ChromeData.Custom.ContainsKey("editable") && !IsFxmContext(args) && (IsRenderingFromComposite(args) || IsPlaceholderFromComposite(args)) && !IsFromPartialDesign(args))
        {
          LayoutModel layoutModel = GetLayoutModel();
          if (layoutModel != null)
          {
            List<RenderingModel> renderingsCollection = layoutModel.Devices[ServiceLocator.ServiceProvider.GetService<IContext>().Device.ID.ToString()].Renderings.RenderingsCollection;
            List<ID> duplicatedRenderingsUniqueIds = renderingsCollection.GetDuplicatedRenderingsUniqueIDs().ToList();
            RenderingReference renderingReference;
            if ((renderingReference = (args.CustomData["renderingReference"] as RenderingReference)) != null && IsDuplicated(duplicatedRenderingsUniqueIds, new ID(renderingReference.UniqueId)))
            {
              return;
            }
            string phKey;
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

          string id = CacheKey(args);
          DictionaryCacheValue dictionaryCacheValue = DictionaryCache.Get(id);

          if (dictionaryCacheValue != null && dictionaryCacheValue.Properties.Keys.Count > 0)
          {
            args.ChromeData.Custom["editable"] = dictionaryCacheValue.Properties["editable"];// Sitecore.Support.310367.312679, loads editable from cache instead of hardcoding
          }
          else
          {
            args.ChromeData.Custom["editable"] = true.ToString().ToLowerInvariant();// Original
          }
        }
      }
    }
    protected string CacheKey(GetChromeDataArgs args)
    {
      string arg = args.CustomData["placeHolderKey"] as string;
      return string.Format("SXA::GetPlaceholderChromeData::{0}:{1}:{2}", arg, args.Item.ID, args.ChromeType);
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
  }
}