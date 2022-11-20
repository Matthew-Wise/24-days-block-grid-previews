namespace _24Grid.Core.Services;

using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Umbraco.Cms.Core.PropertyEditors;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Extensions;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using _24Grid.Core.Helpers;

public interface IBackOfficePreviewService
{
    Task<string> GetMarkupForBlock(
        BlockItemData blockData,
        bool isGrid,
        ControllerContext controllerContext);
}

public sealed class BackOfficePreviewService : IBackOfficePreviewService
{
    private readonly BlockEditorConverter _blockEditorConverter;

    private readonly ITempDataProvider _tempDataProvider;

    private readonly ITypeFinder _typeFinder;

    private readonly IPublishedValueFallback _publishedValueFallback;

    private readonly IViewComponentHelperWrapper _viewComponentHelperWrapper;

    private readonly IViewComponentSelector _viewComponentSelector;

    private readonly IRazorViewEngine _razorViewEngine;

    public BackOfficePreviewService(
        BlockEditorConverter blockEditorConverter,
        ITempDataProvider tempDataProvider,
        ITypeFinder typeFinder,
        IPublishedValueFallback publishedValueFallback,
        IViewComponentHelperWrapper viewComponentHelperWrapper,
        IViewComponentSelector viewComponentSelector,
        IRazorViewEngine razorViewEngine)
    {
        _blockEditorConverter = blockEditorConverter;
        _tempDataProvider = tempDataProvider;
        _typeFinder = typeFinder;
        _publishedValueFallback = publishedValueFallback;
        _viewComponentHelperWrapper = viewComponentHelperWrapper;
        _viewComponentSelector = viewComponentSelector;
        _razorViewEngine = razorViewEngine;
    }

    public async Task<string> GetMarkupForBlock(
        BlockItemData blockData,
        bool isGrid,
        ControllerContext controllerContext)
    {
        var element = _blockEditorConverter.ConvertToElement(blockData, PropertyCacheLevel.None, true);
        if (element == null)
        {
            throw new InvalidOperationException($"Unable to find Element {blockData.ContentTypeAlias}");
        }

        var blockType = _typeFinder.FindClassesWithAttribute<PublishedModelAttribute>().FirstOrDefault(
            x => x.GetCustomAttribute<PublishedModelAttribute>(false)?.ContentTypeAlias == element.ContentType.Alias);

        if (blockType == null)
        {
            throw new InvalidOperationException($"Unable to find BlockType {element.ContentType.Alias}");
        }

        // create instance of the models builder type based from the element
        var blockInstance = Activator.CreateInstance(blockType, element, _publishedValueFallback);

        ViewDataDictionary viewData;
        var contentAlias = element.ContentType.Alias.ToFirstUpper();
        var viewComponent = _viewComponentSelector.SelectComponent(contentAlias);


        string paritalPath;
        if (isGrid)
        {
            viewData = CreateViewDataForGrid(blockType, blockData, blockInstance);
            paritalPath = $"/Views/Partials/blockGrid/Components/{contentAlias}.cshtml";
        }
        else
        {
            viewData = CreateViewDataForList(blockType, blockData, blockInstance);
            paritalPath = $"/Views/Partials/blocklist/Components/{contentAlias}.cshtml";
        }

        if (viewComponent != null)
        {
            return await GetMarkupFromViewComponent(controllerContext, viewData, viewComponent);
        }

        return await GetMarkFromPartial(controllerContext, viewData, paritalPath);
    }

    private static ViewDataDictionary CreateViewDataForGrid(Type blockType, BlockItemData blockData, object? blockInstance)
    {
        var itemType = typeof(BlockGridItem<>).MakeGenericType(blockType);
        var gridItem = (BlockGridItem?)Activator.CreateInstance(
            itemType,
            blockData.Udi,
            blockInstance,
            null,
            null);

        return new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = gridItem
        };
    }

    private static ViewDataDictionary CreateViewDataForList(Type blockType, BlockItemData blockData, object? blockInstance)
    {
        var blockListItemType = typeof(BlockListItem<>).MakeGenericType(blockType);

        var blockListItem = (BlockListItem?)Activator.CreateInstance(
            blockListItemType,
            blockData.Udi,
            blockInstance,
            null,
            null);

        return new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = blockListItem
        };
    }

    private async Task<string> GetMarkFromPartial(ControllerContext controllerContext,
        ViewDataDictionary viewData, string viewName)
    {

        var actionContext = new ActionContext(controllerContext.HttpContext, new RouteData(), new ActionDescriptor());
        await using var sw = new StringWriter();
        var viewResult = _razorViewEngine.GetView(viewName, viewName, false);
        if (viewResult?.View != null)
        {
            var viewContext = new ViewContext(actionContext, viewResult.View, viewData,
                new TempDataDictionary(actionContext.HttpContext, _tempDataProvider), sw, new HtmlHelperOptions());
            await viewResult.View.RenderAsync(viewContext);
        }

        return sw.ToString();
    }

    private async Task<string> GetMarkupFromViewComponent(ControllerContext controllerContext,
        ViewDataDictionary viewData,
        ViewComponentDescriptor viewComponent)
    {
        await using var sw = new StringWriter();
        var viewContext = new ViewContext(
            controllerContext,
            new FakeView(),
            viewData,
            new TempDataDictionary(controllerContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());
        _viewComponentHelperWrapper.Contextualize(viewContext);

        var result = await _viewComponentHelperWrapper.InvokeAsync(viewComponent.TypeInfo.AsType(), viewData.Model);
        result.WriteTo(sw, HtmlEncoder.Default);
        return sw.ToString();
    }

    private sealed class FakeView : IView
    {
        public string Path => string.Empty;

        public Task RenderAsync(ViewContext context)
        {
            return Task.CompletedTask;
        }
    }
}
