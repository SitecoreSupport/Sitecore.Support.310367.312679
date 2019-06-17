namespace Sitecore.Support.XA.Foundation.Editing.Pipelines.GetChromeData
{
// GetPlaceholderChromeData
    using Microsoft.Extensions.DependencyInjection;
    using Sitecore;
    using Sitecore.Configuration;
    using Sitecore.Data;
    using Sitecore.Data.Fields;
    using Sitecore.Data.Items;
    using Sitecore.DependencyInjection;
    using Sitecore.Diagnostics;
    using Sitecore.Layouts;
    using Sitecore.Pipelines;
    using Sitecore.Pipelines.GetChromeData;
    using Sitecore.Pipelines.GetPlaceholderRenderings;
    using Sitecore.StringExtensions;
    using Sitecore.Web;
    using Sitecore.Web.UI.PageModes;
    using Sitecore.XA.Foundation.Abstractions;
    using Sitecore.XA.Foundation.Abstractions.Configuration;
    using Sitecore.XA.Foundation.Caching;
    using Sitecore.XA.Foundation.Editing;
    using Sitecore.XA.Foundation.Multisite.Extensions;
    using Sitecore.XA.Foundation.PlaceholderSettings;
    using Sitecore.XA.Foundation.PlaceholderSettings.Services;
    using Sitecore.XA.Foundation.Presentation;
    using Sitecore.XA.Foundation.Presentation.Layout;
    using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web;

    public class GetPlaceholderChromeData : Sitecore.XA.Foundation.Editing.Pipelines.GetChromeData.GetPlaceholderChromeData
    {
        public GetPlaceholderChromeData(ILayoutsPageContext layoutsPageContext, IContext context) : base(layoutsPageContext, context) { }

        public override void Process(GetChromeDataArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            Assert.IsNotNull(args.ChromeData, "Chrome Data");
            if ("placeholder".Equals(args.ChromeType, StringComparison.OrdinalIgnoreCase))
            {
                if (Context.Site.IsSxaSite())
                {
                    string placeholderKey = args.CustomData["placeHolderKey"] as string;
                    Assert.ArgumentNotNull(placeholderKey, "CustomData[\"{0}\"]".FormatWith("placeHolderKey"));
                    if (CacheEnabled)
                    {
                        string id = CacheKey(args);
                        DictionaryCacheValue dictionaryCacheValue = DictionaryCache.Get(id);
                        if (dictionaryCacheValue != null && dictionaryCacheValue.Properties.Keys.Count > 0 &&
                            ValidateCommands(dictionaryCacheValue, placeholderKey))
                        {
                            AssignCachedProperties(args, dictionaryCacheValue);
                            if (!SxaRenderingSourcesKeysExists())
                            {
                                GetRenderings(args);
                            }

                            return;
                        }
                    }

                    args.ChromeData.DisplayName = StringUtil.GetLastPart(placeholderKey, '/', placeholderKey);
                    if (!GetRenderings(args).Any(delegate(RenderingReference r)
                    {
                        if (!r.Placeholder.Equals(placeholderKey))
                        {
                            return r.Placeholder.Equals("/" + placeholderKey);
                        }

                        return true;
                    }))
                    {
                        List<WebEditButton> buttons =
                            GetButtons("/sitecore/content/Applications/WebEdit/Default Placeholder Buttons");
                        AddButtonsToChromeData(buttons, args);
                    }

                    Item item = null;
                    bool hasPlaceholderSettings = false;
                    if (args.Item != null)
                    {
                        string layout = ChromeContext.GetLayout(args.Item);
                        GetPlaceholderRenderingsArgs getPlaceholderRenderingsArgs =
                            new GetPlaceholderRenderingsArgs(placeholderKey, layout, args.Item.Database)
                            {
                                OmitNonEditableRenderings = true
                            };
                        CorePipeline.Run("getPlaceholderRenderings", getPlaceholderRenderingsArgs);
                        hasPlaceholderSettings = getPlaceholderRenderingsArgs.HasPlaceholderSettings;
                        List<Item> placeholderRenderings = getPlaceholderRenderingsArgs.PlaceholderRenderings;
                        List<string> value = ((placeholderRenderings != null)
                                                 ? (from i in placeholderRenderings
                                                     select i.ID.ToShortID().ToString()).ToList()
                                                 : null) ?? new List<string>();
                        if (!args.ChromeData.Custom.ContainsKey("allowedRenderings"))
                        {
                            args.ChromeData.Custom.Add("allowedRenderings", value);
                        }

                        IList<Item> sxaPlaceholderItems = LayoutsPageContext.GetSxaPlaceholderItems(layout,
                            placeholderKey, // 310367, 312679 sc902x181 specific: patched to use 'placeholderKey' instead of 'getPlaceholderRenderingArgs.PlaceholderKey'
                            args.Item, Context.Device.ID);
                        if (sxaPlaceholderItems.Any())
                        {
                            item = sxaPlaceholderItems.FirstOrDefault();
                        }
                        else
                        {
                            Item sxaPlaceholderItem =
                                LayoutsPageContext.GetSxaPlaceholderItem(
                                    placeholderKey, // 310367, 312679 sc902x181 specific: patched to use 'placeholderKey' instead of 'getPlaceholderRenderingArgs.PlaceholderKey'
                                    args.Item);
                            item = ((sxaPlaceholderItem == null)
                                ? LayoutsPageContext.GetPlaceholderItem(getPlaceholderRenderingsArgs.PlaceholderKey,
                                    args.Item.Database, layout)
                                : sxaPlaceholderItem);
                        }

                        if (!getPlaceholderRenderingsArgs.PlaceholderKey.EndsWith("*", StringComparison.Ordinal) ||
                            sxaPlaceholderItems.Any())
                        {
                            ID result;
                            if (ID.TryParse(
                                    ServiceLocator.ServiceProvider
                                        .GetService<IConfiguration<PlaceholderSettingsConfiguration>>()
                                        .GetConfiguration().FallbackPlaceholderItem, out result) && result == item.ID)
                            {
                                args.ChromeData.DisplayName = getPlaceholderRenderingsArgs.PlaceholderKey;
                            }
                            else
                            {
                                args.ChromeData.DisplayName = ((item == null)
                                    ? StringUtil.GetLastPart(getPlaceholderRenderingsArgs.PlaceholderKey, '/',
                                        getPlaceholderRenderingsArgs.PlaceholderKey)
                                    : HttpUtility.HtmlEncode(item.DisplayName));
                            }
                        }
                    }
                    else if (!args.ChromeData.Custom.ContainsKey("allowedRenderings"))
                    {
                        args.ChromeData.Custom.Add("allowedRenderings", new List<string>());
                    }

                    SetEditableChromeDataItem(args, item, hasPlaceholderSettings);
                    if (CacheEnabled)
                    {
                        string cacheKey = CacheKey(args);
                        StoreInCache(cacheKey, args);
                    }
                }
                else
                {
                    base.Process(args);
                }
            }
        }
    }
}