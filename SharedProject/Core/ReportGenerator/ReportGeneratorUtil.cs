﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExCSS;
using FineCodeCoverage.Core.Utilities;
using FineCodeCoverage.Options;
using FineCodeCoverage.Output;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReportGeneratorPlugins;

namespace FineCodeCoverage.Engine.ReportGenerator
{
	interface IReportGeneratorUtil
    {
		void Initialize(string appDataFolder);
		string ProcessUnifiedHtml(string htmlForProcessing,string reportOutputFolder);
		Task<ReportGeneratorResult> GenerateAsync(IEnumerable<string> coverOutputFiles,string reportOutputFolder, bool throwError = false);

	}

	internal class ReportGeneratorResult
	{
		public string UnifiedHtml { get; set; }
		public string UnifiedXmlFile { get; set; }
		public bool Success { get; set; }
	}

	[Export(typeof(IReportGeneratorUtil))]
	internal partial class ReportGeneratorUtil : IReportGeneratorUtil
	{
		private readonly IAssemblyUtil assemblyUtil;
		private readonly IProcessUtil processUtil;
		private readonly ILogger logger;
		private readonly IToolFolder toolFolder;
		private readonly IToolZipProvider toolZipProvider;
        private readonly IReportColoursProvider reportColoursProvider;
        private readonly IFileUtil fileUtil;
		private readonly IAppOptionsProvider appOptionsProvider;
		private const string zipPrefix = "reportGenerator";
		private const string zipDirectoryName = "reportGenerator";

		private const string ThemeChangedJSFunctionName = "themeChanged";
		private readonly Base64ReportImage plusBase64ReportImage = new Base64ReportImage(".icon-plus", "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPHN2ZyB3aWR0aD0iMTc5MiIgaGVpZ2h0PSIxNzkyIiB2aWV3Qm94PSIwIDAgMTc5MiAxNzkyIiB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjxwYXRoIGQ9Ik0xNjAwIDczNnYxOTJxMCA0MC0yOCA2OHQtNjggMjhoLTQxNnY0MTZxMCA0MC0yOCA2OHQtNjggMjhoLTE5MnEtNDAgMC02OC0yOHQtMjgtNjh2LTQxNmgtNDE2cS00MCAwLTY4LTI4dC0yOC02OHYtMTkycTAtNDAgMjgtNjh0NjgtMjhoNDE2di00MTZxMC00MCAyOC02OHQ2OC0yOGgxOTJxNDAgMCA2OCAyOHQyOCA2OHY0MTZoNDE2cTQwIDAgNjggMjh0MjggNjh6Ii8+PC9zdmc+");
		private readonly Base64ReportImage minusBase64ReportImage = new Base64ReportImage(".icon-minus", "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4NCjxzdmcgd2lkdGg9IjE3OTIiIGhlaWdodD0iMTc5MiIgdmlld0JveD0iMCAwIDE3OTIgMTc5MiIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cGF0aCBmaWxsPSIjMDAwIiBkPSJNMTYwMCA3MzZ2MTkycTAgNDAtMjggNjh0LTY4IDI4aC0xMjE2cS00MCAwLTY4LTI4dC0yOC02OHYtMTkycTAtNDAgMjgtNjh0NjgtMjhoMTIxNnE0MCAwIDY4IDI4dDI4IDY4eiIvPjwvc3ZnPg==");
		private readonly Base64ReportImage downActiveBase64ReportImage = new Base64ReportImage(".icon-down-dir_active", "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4NCjxzdmcgd2lkdGg9IjE3OTIiIGhlaWdodD0iMTc5MiIgdmlld0JveD0iMCAwIDE3OTIgMTc5MiIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cGF0aCBmaWxsPSIjMDA3OEQ0IiBkPSJNMTQwOCA3MDRxMCAyNi0xOSA0NWwtNDQ4IDQ0OHEtMTkgMTktNDUgMTl0LTQ1LTE5bC00NDgtNDQ4cS0xOS0xOS0xOS00NXQxOS00NSA0NS0xOWg4OTZxMjYgMCA0NSAxOXQxOSA0NXoiLz48L3N2Zz4=");
		private readonly Base64ReportImage downInactiveBase64ReportImage = new Base64ReportImage(".icon-down-dir", "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPHN2ZyB3aWR0aD0iMTc5MiIgaGVpZ2h0PSIxNzkyIiB2aWV3Qm94PSIwIDAgMTc5MiAxNzkyIiB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjxwYXRoIGQ9Ik0xNDA4IDcwNHEwIDI2LTE5IDQ1bC00NDggNDQ4cS0xOSAxOS00NSAxOXQtNDUtMTlsLTQ0OC00NDhxLTE5LTE5LTE5LTQ1dDE5LTQ1IDQ1LTE5aDg5NnEyNiAwIDQ1IDE5dDE5IDQ1eiIvPjwvc3ZnPg==");
		private readonly Base64ReportImage upActiveBase64ReportImage = new Base64ReportImage(".icon-up-dir_active", "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4NCjxzdmcgd2lkdGg9IjE3OTIiIGhlaWdodD0iMTc5MiIgdmlld0JveD0iMCAwIDE3OTIgMTc5MiIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cGF0aCBmaWxsPSIjMDA3OEQ0IiBkPSJNMTQwOCAxMjE2cTAgMjYtMTkgNDV0LTQ1IDE5aC04OTZxLTI2IDAtNDUtMTl0LTE5LTQ1IDE5LTQ1bDQ0OC00NDhxMTktMTkgNDUtMTl0NDUgMTlsNDQ4IDQ0OHExOSAxOSAxOSA0NXoiLz48L3N2Zz4=");
        private readonly IScriptInvoker scriptInvoker;
		private IReportColours reportColours;
		private readonly bool showBranchCoverage = true;

		public string ReportGeneratorExePath { get; private set; }

		[ImportingConstructor]
		public ReportGeneratorUtil(
			IAssemblyUtil assemblyUtil,
			IProcessUtil processUtil,
			ILogger logger,
			IToolFolder toolFolder,
			IToolZipProvider toolZipProvider,
			IFileUtil fileUtil,
			IAppOptionsProvider appOptionsProvider,
			IReportColoursProvider reportColoursProvider,
			IScriptInvoker scriptInvoker
			)
		{
			this.fileUtil = fileUtil;
			this.appOptionsProvider = appOptionsProvider;
			this.assemblyUtil = assemblyUtil;
			this.processUtil = processUtil;
			this.logger = logger;
			this.toolFolder = toolFolder;
			this.toolZipProvider = toolZipProvider;
			this.reportColoursProvider = reportColoursProvider;
            this.reportColoursProvider.ColoursChanged += ReportColoursProvider_ColoursChanged;
			this.scriptInvoker = scriptInvoker;
		}

        public void Initialize(string appDataFolder)
		{
			var zipDestination = toolFolder.EnsureUnzipped(appDataFolder, zipDirectoryName, toolZipProvider.ProvideZip(zipPrefix));
			ReportGeneratorExePath = Directory.GetFiles(zipDestination, "reportGenerator.exe", SearchOption.AllDirectories).FirstOrDefault()
								  ?? Directory.GetFiles(zipDestination, "*reportGenerator*.exe", SearchOption.AllDirectories).FirstOrDefault();
		}

		public async Task<ReportGeneratorResult> GenerateAsync(IEnumerable<string> coverOutputFiles, string reportOutputFolder, bool throwError = false)
		{
			var title = "ReportGenerator Run";

			var unifiedHtmlFile = Path.Combine(reportOutputFolder, "index.html");
			var unifiedXmlFile = Path.Combine(reportOutputFolder, "Cobertura.xml");

			var reportGeneratorSettings = new List<string>();

			reportGeneratorSettings.Add($@"""-targetdir:{reportOutputFolder}""");

			async Task<bool> run(string outputReportType, string inputReports)
			{
				var reportTypeSettings = reportGeneratorSettings.ToArray().ToList();

				if (outputReportType.Equals("Cobertura", StringComparison.OrdinalIgnoreCase))
				{
					reportTypeSettings.Add($@"""-reports:{inputReports}""");
					reportTypeSettings.Add($@"""-reporttypes:Cobertura""");

				}
				else if (outputReportType.Equals("HtmlInline_AzurePipelines", StringComparison.OrdinalIgnoreCase))
				{
					reportTypeSettings.Add($@"""-reports:{inputReports}""");
					reportTypeSettings.Add($@"""-plugins:{typeof(FccLightReportBuilder).Assembly.Location}""");
					reportTypeSettings.Add($@"""-reporttypes:{FccLightReportBuilder.REPORT_TYPE}""");
					var (cyclomaticThreshold, crapScoreThreshold, nPathThreshold) = HotspotThresholds();

					reportTypeSettings.Add($@"""riskHotspotsAnalysisThresholds:metricThresholdForCyclomaticComplexity={cyclomaticThreshold}""");
					reportTypeSettings.Add($@"""riskHotspotsAnalysisThresholds:metricThresholdForCrapScore={crapScoreThreshold}""");
					reportTypeSettings.Add($@"""riskHotspotsAnalysisThresholds:metricThresholdForNPathComplexity={nPathThreshold}""");

				}
				else
				{
					throw new Exception($"Unknown reporttype '{outputReportType}'");
				}

				logger.Log($"{title} Arguments [reporttype:{outputReportType}] {Environment.NewLine}{string.Join($"{Environment.NewLine}", reportTypeSettings)}");

				var result = await processUtil
					.ExecuteAsync(new ExecuteRequest
					{
						FilePath = ReportGeneratorExePath,
						Arguments = string.Join(" ", reportTypeSettings),
						WorkingDirectory = reportOutputFolder
					});


				if (result != null)
				{
					if (result.ExitCode != 0)
					{
						logger.Log($"{title} [reporttype:{outputReportType}] Error", result.Output);
						logger.Log($"{title} [reporttype:{outputReportType}] Error", result.ExitCode);

						if (throwError)
						{
							throw new Exception(result.Output);
						}

						return false;
					}

					logger.Log($"{title} [reporttype:{outputReportType}]", result.Output);
					return true;
				}
				return false;

			}

			var reportGeneratorResult = new ReportGeneratorResult { Success = false, UnifiedHtml = null, UnifiedXmlFile = unifiedXmlFile };

			var coberturaResult = await run("Cobertura", string.Join(";", coverOutputFiles));

			if (coberturaResult)
			{
				var htmlResult = await run("HtmlInline_AzurePipelines", unifiedXmlFile);
				if (htmlResult)
				{
					reportGeneratorResult.UnifiedHtml = fileUtil.ReadAllText(unifiedHtmlFile);
					reportGeneratorResult.Success = true;
				}

			}

			return reportGeneratorResult;

		}

		private void SetInitialTheme(HtmlAgilityPack.HtmlDocument document)
		{
			
			var backgroundColor = ToJsColour(reportColours.BackgroundColour);
			var fontColour = ToJsColour(reportColours.FontColour);
			var overviewTableBorderColor = ToJsColour(reportColours.TableBorderColour);

			var style = document.DocumentNode.Descendants("style").First();

			var parser = new StylesheetParser();
			var stylesheet = parser.Parse(style.InnerHtml);
			var styleRules = stylesheet.StyleRules;

			var lightRedForCyclomatic = styleRules.First(r => r.SelectorText == ".lightred");
			lightRedForCyclomatic.Style.BackgroundColor = null;
			/*
                Other option
                lightRedForCyclomatic.Style.BackgroundColor = "rgba(255,255,255,0.3)";

                or 
                be *dynamic* and be lighten / darken %age of the background color
            */
			var grayRule = styleRules.First(r => r.SelectorText == ".gray");
			grayRule.Style.BackgroundColor = ToJsColour(reportColours.GrayCoverage);

			var htmlRule = styleRules.First(r => r.Selector.Text == "html");
			var htmlStyle = htmlRule.Style;
			htmlStyle.BackgroundColor = backgroundColor;

			var containerRule = styleRules.First(r => r.SelectorText == ".container");
			containerRule.Style.BackgroundColor = backgroundColor;

			var overviewTableBorder = $"1px solid {overviewTableBorderColor}";
			var overviewThRule = styleRules.First(r => r.SelectorText == ".overview th");
			overviewThRule.Style.BackgroundColor = backgroundColor;
			overviewThRule.Style.Border = overviewTableBorder;

			var overviewTdRule = styleRules.First(r => r.SelectorText == ".overview td");
			overviewTdRule.Style.Border = overviewTableBorder;

			var overviewRule = styleRules.First(r => r.SelectorText == ".overview");
			overviewRule.Style.Border = overviewTableBorder;

			var overviewHeaderLinks = styleRules.First(r => r.SelectorText == ".overview th a");
			overviewHeaderLinks.Style.Color = ToJsColour(reportColours.CoverageTableHeaderFontColour);

			var overviewTrHoverRule = styleRules.First(r => r.SelectorText == ".overview tr:hover");
			overviewTrHoverRule.Style.Background = ToJsColour(reportColours.CoverageTableRowHoverBackgroundColour);

			var expandCollapseIconColor = reportColours.CoverageTableExpandCollapseIconColour;
			plusBase64ReportImage.FillSvg(styleRules, ToJsColour(expandCollapseIconColor));
			minusBase64ReportImage.FillSvg(styleRules, ToJsColour(expandCollapseIconColor));

			var coverageTableActiveSortColor = ToJsColour(reportColours.CoverageTableActiveSortColour);
			var coverageTableInactiveSortColor = ToJsColour(reportColours.CoverageTableInactiveSortColour);
			downActiveBase64ReportImage.FillSvg(styleRules, coverageTableActiveSortColor);
			upActiveBase64ReportImage.FillSvg(styleRules, coverageTableActiveSortColor);
			downInactiveBase64ReportImage.FillSvg(styleRules, coverageTableInactiveSortColor);

			var linkColor = ToJsColour(reportColours.LinkColour);
			var linkRule = styleRules.First(r => r.SelectorText == "a");
			var linkHoverRule = styleRules.First(r => r.SelectorText == "a:hover");

			linkRule.Style.Color = linkColor;
			linkRule.Style.Cursor = "pointer";
			linkRule.Style.TextDecoration = "none";

			linkHoverRule.Style.Color = linkColor;
			linkHoverRule.Style.Cursor = "pointer";
			linkHoverRule.Style.TextDecoration = "none";

			var stringWriter = new StringWriter();
			var formatter = new CompressedStyleFormatter();
			stylesheet.ToCss(stringWriter, formatter);
			var changedCss = stringWriter.ToString();
			style.InnerHtml = changedCss;
		}

		private string GetStickyTableHead()
		{
            if (!appOptionsProvider.Get().StickyCoverageTable)
            {
				return "";
            }
			return @"
function CustomEventPolyfill() {
  if ( typeof window.CustomEvent === 'function' ) return false;

  function CustomEvent( event, params ) {
      params = params || { bubbles: false, cancelable: false, detail: null };
			var evt = document.createEvent('CustomEvent');
			evt.initCustomEvent( event, params.bubbles, params.cancelable, params.detail );
      return evt;
		}

		window.CustomEvent = CustomEvent;
}

	function DataStore()
	{
		return {
		_storage: new WeakMap(),
    put: function(element, key, obj) {
				if (!this._storage.has(element))
				{
					this._storage.set(element, new Map());
				}
				this._storage.get(element).set(key, obj);
			},
    get: function(element, key) {
				let data = this._storage.get(element)

	  return data && data.get(key);
			},
    remove: function(element, key) {
				let data = this._storage.get(element)

	  if (!data) { return }
				let ret = data.delete(key);
				if (!data.size === 0)
				{
					this._storage.delete(element);
				}
				return ret;
			}
		}
	}

	function extendObj(defaultObj, overrideObj)
	{
		var newObj = defaultObj
	  Object.keys(overrideObj || { }).forEach(function(k) {
			newObj[k] = overrideObj[k]
  });

		return newObj
	}

	function getOffset(el)
	{
		var rect = el.getBoundingClientRect();
		return {
		top: rect.top + window.pageYOffset,
    left: rect.left + window.pageXOffset,
  };
	}

	function getHeight(el)
	{
		if (el == window)
		{
			return window.innerHeight
	    }
		if (el == document)
		{
			return Math.max(
			  document.documentElement['clientHeight'],
			  document.body['scrollHeight'],
			  document.documentElement['scrollHeight'],
			  document.body['offsetHeight'],
			  document.documentElement['offsetHeight']
			);
		}
		var height = parseFloat(getComputedStyle(el, null).height.replace('px', ''))
	  return height ? height : el.offsetHeight
	}

	function getWidth(el)
	{
		if (el == window)
		{
			return window.innerWidth
	    }
		if (el == document)
		{
			return Math.max(
			  document.documentElement['clientWidth'],
			  document.body['scrollWidth'],
			  document.documentElement['scrollWidth'],
			  document.body['offsetWidth'],
			  document.documentElement['offsetWidth']
			);
		}
		var width = parseFloat(getComputedStyle(el, null).width.replace('px', ''))
	  return width ? width : el.offsetWidth
	}

	function setStyles(el, propertyObject)
	{
  for (var property in propertyObject)
			el.style[property] = propertyObject[property];
	}

	function fireEvent(name, el, data)
	{
		var details = data ? { } : { details: data }
		var evt = new CustomEvent(name, details)
	  el.dispatchEvent(evt)
	}

	var dataStore = DataStore();
	window.dataStore = dataStore
	CustomEventPolyfill()

function stickytheadapply(elements, options)
	{
		var name = 'stickyThead',
		  id = 0,
		  defaults = {
	  fixedOffset: 0,
      leftOffset: 0,
      marginTop: 0,
      objDocument: document,
      objHead: document.head,
      objWindow: window,
      scrollableArea: window,
      cacheHeaderHeight: false,
      zIndex: 3

	};

	function Plugin(el, options)
	{
		// To avoid scope issues, use 'base' instead of 'this'
		// to reference this class from internal events and functions.
		var base = this;

		// Access to jQuery and DOM versions of element
		// base.$el = $(el);
		base.el = el;
		base.id = id++;

		// Cache DOM refs for performance reasons
		base.$clonedHeader = null;
		base.$originalHeader = null;

		// Cache header height for performance reasons
		base.cachedHeaderHeight = null;

		// Keep track of state
		base.isSticky = false;
		base.hasBeenSticky = false;
		base.leftOffset = null;
		base.topOffset = null;

		base.init = function() {
			base.setOptions(options);

			// base.$el.each(function () {
			// var $this = $(this);

			// remove padding on <table> to fix issue #7
			base.el.style.padding = '0px';

			base.$originalHeader = base.el.querySelector('thead');
			base.$clonedHeader = base.$originalHeader.cloneNode(true);
			// dispatchEvent
			fireEvent('clonedHeader.' + name, base.el, base.$clonedHeader)
   

	  base.$clonedHeader.setAttribute('class', 'tableFloatingHeader');
			setStyles(base.$clonedHeader, { display: 'none', opacity: 0 })

      base.$originalHeader.setAttribute('class', 'tableFloatingHeaderOriginal');

			base.$originalHeader.insertAdjacentElement('afterend', base.$clonedHeader);

			var style = document.createElement('style')
   
	  style.setAttribute('type', 'text/css')
   
	  style.setAttribute('media', 'print')
   
	  style.innerHTML = '.tableFloatingHeader{display:none !important;}' +
		'.tableFloatingHeaderOriginal{position:static !important;}'
   
	  base.$printStyle = style
   
	  base.$head.appendChild(base.$printStyle);


			base.$clonedHeader.querySelectorAll('input, select').forEach(function(el) {
				el.setAttribute('disabled', true);
			})

      base.updateWidth();
			base.toggleHeaders();
			base.bind();
		};

		base.destroy = function() {
			base.el && base.el.removeEventListener('destroyed', base.teardown);
			base.teardown();
		};

		base.teardown = function() {
			if (base.isSticky)
			{
				setStyles(base.$originalHeader, { position: 'static' });
			}
			dataStore.remove(base.el, name)
   
	  base.unbind();

			base.$clonedHeader.parentNode.removeChild(base.$clonedHeader);
			base.$originalHeader.classList.remove('tableFloatingHeaderOriginal');
			setStyles(base.$originalHeader, { visibility: 'visible' });
			base.$printStyle.parentNode.removeChild(base.$printStyle);

			base.el = null;
			base.$el = null;
		};

		base.bind = function() {
			base.$scrollableArea.addEventListener('scroll', base.toggleHeaders);
			if (!base.isWindowScrolling)
			{
				base.$window.addEventListener('scroll', base.setPositionValues);
				base.$window.addEventListener('resize', base.toggleHeaders);
			}
			base.$scrollableArea.addEventListener('resize', base.toggleHeaders);
			base.$scrollableArea.addEventListener('resize', base.updateWidth);
		};

		base.unbind = function() {
			// unbind window events by specifying handle so we don't remove too much
			base.$scrollableArea.removeEventListener('scroll', base.toggleHeaders);
			if (!base.isWindowScrolling)
			{
				base.$window.removeEventListener('scroll', base.setPositionValues);
				base.$window.removeEventListener('resize', base.toggleHeaders);
			}
			base.$scrollableArea.removeEventListener('resize', base.updateWidth);
		};

		base.toggleHeaders = function() {
			if (base.el)
			{
				var newLeft,
				  newTopOffset = base.isWindowScrolling ? (
					isNaN(base.options.fixedOffset) ?
					  base.options.fixedOffset.offsetHeight :
					  base.options.fixedOffset
				  ) :
					getOffset(base.$scrollableArea).top + (!isNaN(base.options.fixedOffset) ? base.options.fixedOffset : 0),
				  offset = getOffset(base.el),

				  scrollTop = base.$scrollableArea.pageYOffset + newTopOffset,
          scrollLeft = base.$scrollableArea.pageXOffset,
          headerHeight,

          scrolledPastTop = base.isWindowScrolling ?
			scrollTop > offset.top :
			newTopOffset > offset.top,
          notScrolledPastBottom;

				if (scrolledPastTop)
				{
					headerHeight = base.options.cacheHeaderHeight ? base.cachedHeaderHeight : getHeight(base.$originalHeader);
					notScrolledPastBottom = (base.isWindowScrolling ? scrollTop : 0) <
					  (offset.top + getHeight(base.el) - headerHeight - (base.isWindowScrolling ? 0 : newTopOffset));
				}

				if (scrolledPastTop && notScrolledPastBottom)
				{
					newLeft = offset.left - scrollLeft + base.options.leftOffset;
					setStyles(base.$originalHeader, {
					position: 'fixed',
            marginTop: base.options.marginTop + 'px',
            top: 0,
            left: newLeft + 'px',
            zIndex: base.options.zIndex
		  
		  });
					base.leftOffset = newLeft;
					base.topOffset = newTopOffset;
					base.$clonedHeader.style.display = '';
					if (!base.isSticky)
					{
						base.isSticky = true;
						// make sure the width is correct: the user might have resized the browser while in static mode
						base.updateWidth();
						fireEvent('enabledStickiness.' + name, base.el)
		  
		  }
					base.setPositionValues();
				}
				else if (base.isSticky)
				{
					base.$originalHeader.style.position = 'static';
					base.$clonedHeader.style.display = 'none';
					base.isSticky = false;
					base.resetWidth(base.$clonedHeader.querySelectorAll('td,th'), base.$originalHeader.querySelectorAll('td,th'));
					fireEvent('disabledStickiness.' + name, base.el)
	  
		}
			}
		};

		base.setPositionValues = function() {
			var winScrollTop = base.$window.pageYOffset,
        winScrollLeft = base.$window.pageXOffset;

			/*if (!base.isSticky ||
			  winScrollTop < 0 || winScrollTop + getHeight(base.$window) > getHeight(base.$document) ||
			  winScrollLeft < 0 || winScrollLeft + getWidth(base.$window) > getWidth(base.$document)) {
			  return;
			}*/
			setStyles(base.$originalHeader, {
			top: base.topOffset - (base.isWindowScrolling ? 0 : winScrollTop) + 'px',
        left: base.leftOffset - (base.isWindowScrolling ? 0 : winScrollLeft) + 'px'
	  
	  });
		};

		base.updateWidth = function() {
			if (!base.isSticky)
			{
				return;
			}
			// Copy cell widths from clone
			if (!base.$originalHeaderCells) {
				base.$originalHeaderCells = base.$originalHeader.querySelectorAll('th,td');
			}
			if (!base.$clonedHeaderCells) {
				base.$clonedHeaderCells = base.$clonedHeader.querySelectorAll('th,td');
			}
			var cellWidths = base.getWidth(base.$clonedHeaderCells);
			base.setWidth(cellWidths, base.$clonedHeaderCells, base.$originalHeaderCells);

			// Copy row width from whole table
			base.$originalHeader.style.width = getWidth(base.$clonedHeader);

			// If we're caching the height, we need to update the cached value when the width changes
			if (base.options.cacheHeaderHeight)
			{
				base.cachedHeaderHeight = getHeight(base.$clonedHeader);
			}
		};

		base.getWidth = function($clonedHeaders) {
			var widths = [];
      $clonedHeaders.forEach(function(el, index) {
				var width;

				if (getComputedStyle(el).boxSizing === 'border-box')
				{
					var boundingClientRect = el.getBoundingClientRect();
					if (boundingClientRect.width)
					{
						width = boundingClientRect.width; // #39: border-box bug
					}
					else
					{
						width = boundingClientRect.right - boundingClientRect.left; // ie8 bug: getBoundingClientRect() does not have a width property
					}
				}
				else
				{
					var $origTh = base.$originalHeader.querySelector('th');
					if ($origTh.style.borderCollapse === 'collapse') {
						if (window.getComputedStyle)
						{
							width = parseFloat(window.getComputedStyle(el, null).width);
						}
						else
						{
							// ie8 only
							var leftPadding = parseFloat(el.style.paddingLeft);
							var rightPadding = parseFloat(el.style.paddingRight);
							// Needs more investigation - this is assuming constant border around this cell and it's neighbours.
							var border = parseFloat(el.style.borderWidth);
							width = el.offsetWidth - leftPadding - rightPadding - border;
						}
					} else
					{
						width = getWidth(el);
					}
				}

				widths[index] = width;
			});
			return widths;
		};

		base.setWidth = function(widths, $clonedHeaders, $origHeaders) {
      $clonedHeaders.forEach(function(_, index) {
				var width = widths[index];
				setStyles($origHeaders[index], {
				minWidth: width + 'px',
          maxWidth: width + 'px'
		
		});
			});
		};

		base.resetWidth = function($clonedHeaders, $origHeaders) {
      $clonedHeaders.forEach(function(_, index) {
				setStyles($origHeaders[index], {
				minWidth: el.style.minWidth,
          maxWidth: el.style.maxWidth
		
		});
			});
		};

		base.setOptions = function(options) {
			base.options = extendObj(defaults, options);
			base.$window = base.options.objWindow;
			base.$head = base.options.objHead;
			base.$document = base.options.objDocument;
			base.$scrollableArea = base.options.scrollableArea;
			base.isWindowScrolling = base.$scrollableArea === base.$window;
		};

		base.updateOptions = function(options) {
			base.setOptions(options);
			// scrollableArea might have changed
			base.unbind();
			base.bind();
			base.updateWidth();
			base.toggleHeaders();
		};

		// Listen for destroyed, call teardown
		base.el.addEventListener('destroyed', base.teardown.bind(base));

		// Run initializer
		base.init();
	}

  return elements.forEach(function (element) {
    var instance = dataStore.get(element, name)
    if (instance) {
      if (typeof options === 'string') {
        instance[options].apply(instance);
} else
{
	instance.updateOptions(options);
}
    } else if (options !== 'destroy')
{
	dataStore.put(element, name, new Plugin(element, options));
}
  });
}

let elements = document.querySelectorAll('table.overview.table-fixed.stripped')
stickytheadapply(elements, { fixedOffset: document.getElementById('divHeader') });
";
		}

		private string GetGroupingCss(bool namespacedClasses)
        {
            if (namespacedClasses)
            {
				return "";
            }
			return HideGroupingCss();

		}

		private string HideGroupingCss()
        {
			return @"
coverage-info div.customizebox div:nth-child(2) { visibility:hidden;font-size:1px;height:1px;padding:0;border:0;margin:0 }
coverage-info div.customizebox div:nth-child(2) * { visibility:hidden;font-size:1px;height:1px;padding:0;border:0;margin:0 }
";
		}

		private string HackGroupingToAllowAll()
        {
			return @"
				var customizeBox = document.getElementsByClassName('customizebox')[0];
				var groupingInput = customizeBox.querySelector('input');
				groupingInput.max = 1;
";

		}

		public string ProcessUnifiedHtml(string htmlForProcessing, string reportOutputFolder)
		{
			var appOptions = appOptionsProvider.Get();
			var namespacedClasses = appOptions.NamespacedClasses;
			reportColours = reportColoursProvider.GetColours();
			return assemblyUtil.RunInAssemblyResolvingContext(() =>
			{
				var (cyclomaticThreshold, crapScoreThreshold, nPathThreshold) = HotspotThresholds();
				var noRiskHotspotsHeader = "No risk hotspots that exceed options :";
				var noRiskHotspotsCyclomaticMsg = $"Cyclomatic complexity : {cyclomaticThreshold}";
				var noRiskHotspotsNpathMsg =$"NPath complexity      : {nPathThreshold}";
				var noRiskHotspotsCrapMessage = $"Crap score            : {crapScoreThreshold}";
				var doc = new HtmlDocument
				{
					OptionFixNestedTags = true,
					OptionAutoCloseOnEnd = true
				};


				doc.LoadHtml(htmlForProcessing);
				SetInitialTheme(doc);
				htmlForProcessing = null;

				doc.DocumentNode.QuerySelectorAll(".footer").ToList().ForEach(x => x.SetAttributeValue("style", "display:none"));
				doc.DocumentNode.QuerySelectorAll(".container").ToList().ForEach(x => x.SetAttributeValue("style", "margin:0;padding:0;border:0"));
				doc.DocumentNode.QuerySelectorAll(".containerleft").ToList().ForEach(x => x.SetAttributeValue("style", "margin:0;padding:0;border:0"));
				doc.DocumentNode.QuerySelectorAll(".containerleft > h1 , .containerleft > p").ToList().ForEach(x => x.SetAttributeValue("style", "display:none"));

				// DOM changes

				HideRowsFromOverviewTable(doc);
				

				// TEXT changes
				var assemblyClassDelimiter = "!";

				var outerHtml = doc.DocumentNode.OuterHtml;
				var htmlSb = new StringBuilder(outerHtml);

				var assembliesSearch = "var assemblies = [";
				var startIndex = outerHtml.IndexOf(assembliesSearch) + assembliesSearch.Length - 1;
				var endIndex = outerHtml.IndexOf("var historicCoverageExecutionTimes");
				var assembliesToReplace = outerHtml.Substring(startIndex, endIndex - startIndex);
				endIndex = assembliesToReplace.LastIndexOf(']');
				assembliesToReplace = assembliesToReplace.Substring(0, endIndex + 1);

				var assemblies = JArray.Parse(assembliesToReplace);
				foreach (JObject assembly in assemblies)
				{
					var assemblyName = assembly["name"];
					var classes = assembly["classes"] as JArray;

					var autoGeneratedRemovals = new List<JObject>();
					foreach (JObject @class in classes)
					{
						var className = @class["name"].ToString();
						if (className == "AutoGeneratedProgram")
						{
							autoGeneratedRemovals.Add(@class);
						}
						else
						{
							if (!namespacedClasses)
							{
								// simplify name
								var lastIndexOfDotInName = className.LastIndexOf('.');
								if (lastIndexOfDotInName != -1) @class["name"] = className.Substring(lastIndexOfDotInName).Trim('.');
							}
							//mark with # and add the assembly name
							var rp = @class["rp"].ToString();
							var htmlIndex = rp.IndexOf(".html");
							@class["rp"] = $"#{assemblyName}{assemblyClassDelimiter}{className + ".html" + rp.Substring(htmlIndex + 5)}";
						}

					}
					foreach (var autoGeneratedRemoval in autoGeneratedRemovals)
					{
						classes.Remove(autoGeneratedRemoval);
					}

				}
				var assembliesReplaced = assemblies.ToString();
				htmlSb.Replace(assembliesToReplace, assembliesReplaced);

				//is this even present if there are no riskhotspots
				var riskHotspotsSearch = "var riskHotspots = [";
				var rhStartIndex = outerHtml.IndexOf(riskHotspotsSearch) + riskHotspotsSearch.Length - 1;
				var rhEndIndex = outerHtml.IndexOf("var branchCoverageAvailable");
				var rhToReplace = outerHtml.Substring(rhStartIndex, rhEndIndex - rhStartIndex);
				rhEndIndex = rhToReplace.LastIndexOf(']');
				rhToReplace = rhToReplace.Substring(0, rhEndIndex + 1);

				var riskHotspots = JArray.Parse(rhToReplace);
				foreach (JObject riskHotspot in riskHotspots)
				{
					var assembly = riskHotspot["assembly"].ToString();
					var qualifiedClassName = riskHotspot["class"].ToString();
					if (!namespacedClasses)
					{
						// simplify name
						var lastIndexOfDotInName = qualifiedClassName.LastIndexOf('.');
						if (lastIndexOfDotInName != -1) riskHotspot["class"] = qualifiedClassName.Substring(lastIndexOfDotInName).Trim('.');
					}
					var newReportPath = $"#{assembly}{assemblyClassDelimiter}{qualifiedClassName}.html";
					riskHotspot["reportPath"] = newReportPath;
				}
				var riskHotspotsReplaced = riskHotspots.ToString();
				htmlSb.Replace(rhToReplace, riskHotspotsReplaced);

				htmlSb.Replace(".table-fixed", ".table-fixed-ignore-me");

				var fontColour = ToJsColour(reportColours.FontColour);
				var scrollbarThumbColour = ToJsColour(reportColours.ScrollBarThumbColour);
				htmlSb.Replace("</head>", $@"
				<style id=""fccStyle1"" type=""text/css"">
					*, body {{ font-size: small;  color: {fontColour}}}
					table td {{ white-space: nowrap; }}
					table.coverage {{ width:150px;height:13px }}
					body {{ padding-left:3px;padding-right:3px;padding-bottom:3px }}
					table,tr,th,td {{ font-size: small; }}
					body {{ -webkit-user-select:none;-moz-user-select:none;-ms-user-select:none;-o-user-select:none;user-select:none }}
					table.overview th, table.overview td {{ font-size: small; white-space: nowrap; word-break: normal; padding-left:10px;padding-right:10px; }}
					{GetGroupingCss(namespacedClasses)}
					table,tr,th,td {{ border: 1px solid; font-size: small; }}
					input[type=text] {{ color:{ToJsColour(reportColours.TextBoxTextColour)}; background-color:{ToJsColour(reportColours.TextBoxColour)};border-color:{ToJsColour(reportColours.TextBoxBorderColour)} }}
					select {{ color:{ToJsColour(reportColours.ComboBoxTextColour)}; background-color:{ToJsColour(reportColours.ComboBoxColour)};border-color:{ToJsColour(reportColours.ComboBoxBorderColour)} }}
                    body, html {{ scrollbar-arrow-color:{ToJsColour(reportColours.ScrollBarArrowColour)};scrollbar-track-color:{ToJsColour(reportColours.ScrollBarTrackColour)};scrollbar-face-color:{scrollbarThumbColour};scrollbar-shadow-color:{scrollbarThumbColour};scrollbar-highlight-color:{scrollbarThumbColour};scrollbar-3dlight-color:{scrollbarThumbColour};scrollbar-darkshadow-color:{scrollbarThumbColour} }}				
					input[type=range]::-ms-thumb {{
					  background: {scrollbarThumbColour};
					  border: {scrollbarThumbColour}
					}}
					input[type=range]::-ms-track {{
						color: transparent;
						border-color: transparent;
						background: transparent;
					}}
					
					input[type=range]::-ms-fill-lower {{
					  background: {ToJsColour(reportColours.ScrollBarTrackColour)};  
					}}
					input[type=range]::-ms-fill-upper {{
					  background: {ToJsColour(reportColours.ScrollBarTrackColour)}; 
					}}
				</style>
				</head>
			");

				htmlSb.Replace("</body>", $@"
					<script type=""text/javascript"">
						{GetStickyTableHead()}
						{HackGroupingToAllowAll()}
						function getRuleBySelector(cssRules,selector){{
						for(var i=0;i<cssRules.length;i++){{
							if(cssRules[i].selectorText == selector){{
								return cssRules[i];
							}}
						}}
					}}
					function getStyleBySelector(cssRules,selector){{
						return getRuleBySelector(cssRules,selector).style;
					}}

					function getStyleSheetById(id){{
						for(var i=0;i<document.styleSheets.length;i++){{
							var styleSheet = document.styleSheets[i];
							if(styleSheet.ownerNode && styleSheet.ownerNode.id == id){{
								return styleSheet;
							}}
						}}
					}}
					function {ThemeChangedJSFunctionName}(theme){{
							var fccMediaStylesheet = getStyleSheetById('fccMediaStyle');	
							var highContrastRule = fccMediaStylesheet.cssRules[0]
							var highContrastRules = highContrastRule.cssRules
							getStyleBySelector(highContrastRules,'table.coverage > td.gray').setProperty('background-color',theme.{nameof(JsThemeStyling.GrayCoverage)});

							var fccStyleSheet1Rules = getStyleSheetById('fccStyle1').cssRules;		
					
							var rangeInputFillLower = getStyleBySelector(fccStyleSheet1Rules, 'input[type=range]::-ms-fill-lower');
							rangeInputFillLower.setProperty('background',theme.{nameof(JsThemeStyling.ScrollBarTrack)});
							var rangeInputFillUpper = getStyleBySelector(fccStyleSheet1Rules, 'input[type=range]::-ms-fill-upper');
							rangeInputFillUpper.setProperty('background',theme.{nameof(JsThemeStyling.ScrollBarTrack)});
							var rangeInputThumb = getStyleBySelector(fccStyleSheet1Rules, 'input[type=range]::-ms-thumb');
							rangeInputThumb.setProperty('background',theme.{nameof(JsThemeStyling.ScrollBarThumb)});
							rangeInputThumb.setProperty('border',theme.{nameof(JsThemeStyling.ScrollBarThumb)});

							var scrollBarStyle = getStyleBySelector(fccStyleSheet1Rules,'body, html');
							scrollBarStyle.setProperty('scrollbar-arrow-color',theme.{nameof(JsThemeStyling.ScrollBarArrow)});
							scrollBarStyle.setProperty('scrollbar-track-color',theme.{nameof(JsThemeStyling.ScrollBarTrack)});
							scrollBarStyle.setProperty('scrollbar-face-color',theme.{nameof(JsThemeStyling.ScrollBarThumb)});
							scrollBarStyle.setProperty('scrollbar-shadow-color',theme.{nameof(JsThemeStyling.ScrollBarThumb)});
							scrollBarStyle.setProperty('scrollbar-highlight-color',theme.{nameof(JsThemeStyling.ScrollBarThumb)});
							scrollBarStyle.setProperty('scrollbar-3dlight-color',theme.{nameof(JsThemeStyling.ScrollBarThumb)});
							scrollBarStyle.setProperty('scrollbar-darkshadow-color',theme.{nameof(JsThemeStyling.ScrollBarThumb)});

							getStyleBySelector(fccStyleSheet1Rules,'*, body').setProperty('color',theme.{nameof(JsThemeStyling.FontColour)});
							var textStyle = getStyleBySelector(fccStyleSheet1Rules,'input[type=text]');
							textStyle.setProperty('color',theme.{nameof(JsThemeStyling.TextBoxTextColour)});			
							textStyle.setProperty('background-color',theme.{nameof(JsThemeStyling.TextBoxColour)});								
							textStyle.setProperty('border-color',theme.{nameof(JsThemeStyling.TextBoxBorderColour)});

							var comboStyle = getStyleBySelector(fccStyleSheet1Rules,'select');
							comboStyle.setProperty('color',theme.{nameof(JsThemeStyling.ComboBoxText)});		
							comboStyle.setProperty('background-color',theme.{nameof(JsThemeStyling.ComboBox)});	
							comboStyle.setProperty('border-color',theme.{nameof(JsThemeStyling.ComboBoxBorder)});

							var fccStyleSheet2Rules = getStyleSheetById('fccStyle2').cssRules;	
							getStyleBySelector(fccStyleSheet2Rules,'#divHeader').setProperty('background-color',theme.{nameof(JsThemeStyling.DivHeaderBackgroundColour)});							
							var headerTabsStyle = getStyleBySelector(fccStyleSheet2Rules,'table#headerTabs td');
							headerTabsStyle.setProperty('color',theme.{nameof(JsThemeStyling.HeaderFontColour)});
							headerTabsStyle.setProperty('border-color',theme.{nameof(JsThemeStyling.HeaderBorderColour)});
							getStyleBySelector(fccStyleSheet2Rules,'table#headerTabs td.tab').setProperty('background-color',theme.{nameof(JsThemeStyling.TabBackgroundColour)});		

							var mainStyle = document.styleSheets[0];
							var mainRules = mainStyle.cssRules;

							getStyleBySelector(mainRules,'.gray').setProperty('background-color',theme.{nameof(JsThemeStyling.GrayCoverage)});

							getStyleBySelector(mainRules,'html').setProperty('background-color',theme.{nameof(JsThemeStyling.BackgroundColour)});
							getStyleBySelector(mainRules,'.container').setProperty('background-color',theme.{nameof(JsThemeStyling.BackgroundColour)});

							var overviewTableBorder = '1px solid ' + theme.{nameof(JsThemeStyling.TableBorderColour)};
							var overviewStyle = getStyleBySelector(mainRules,'.overview');
							overviewStyle.setProperty('border',overviewTableBorder);
							var overviewThStyle = getStyleBySelector(mainRules,'.overview th');
							overviewThStyle.setProperty('background-color',theme.{nameof(JsThemeStyling.BackgroundColour)});
							overviewThStyle.setProperty('border',overviewTableBorder);
							var overviewTdStyle = getStyleBySelector(mainRules,'.overview td');
							overviewTdStyle.setProperty('border',overviewTableBorder);

							var overviewHeaderLinksStyle = getStyleBySelector(mainRules,'.overview th a');
							overviewHeaderLinksStyle.setProperty('color',theme.{nameof(JsThemeStyling.CoverageTableHeaderFontColour)});

							var overviewTrHoverStyle = getStyleBySelector(mainRules,'.overview tr:hover');
							overviewTrHoverStyle.setProperty('background',theme.{nameof(JsThemeStyling.CoverageTableRowHoverBackgroundColour)});

							var linkStyle = getStyleBySelector(mainRules,'a');
							var linkHoverStyle = getStyleBySelector(mainRules,'a:hover');
							linkStyle.setProperty('color',theme.{nameof(JsThemeStyling.LinkColour)});
							linkHoverStyle.setProperty('color',theme.{nameof(JsThemeStyling.LinkColour)});

							var iconPlusStyle = getStyleBySelector(mainRules,'.icon-plus');
							iconPlusStyle.setProperty('background-image',theme.{nameof(JsThemeStyling.PlusBase64)});
							var iconMinusStyle = getStyleBySelector(mainRules,'.icon-minus');
							iconMinusStyle.setProperty('background-image',theme.{nameof(JsThemeStyling.MinusBase64)});
							var iconDownActiveStyle = getStyleBySelector(mainRules,'.icon-down-dir_active');
							iconDownActiveStyle.setProperty('background-image',theme.{nameof(JsThemeStyling.DownActiveBase64)});
							var iconDownInactiveStyle = getStyleBySelector(mainRules,'.icon-down-dir');
							iconDownInactiveStyle.setProperty('background-image',theme.{nameof(JsThemeStyling.DownInactiveBase64)});
							var iconUpActiveStyle = getStyleBySelector(mainRules,'.icon-up-dir_active');
							iconUpActiveStyle.setProperty('background-image',theme.{nameof(JsThemeStyling.UpActiveBase64)});
					}}
						var htmlExtension = '.html';
						
						var eventListener = function (element, event, func) {{
							if (element.addEventListener)
								element.addEventListener(event, func, false);
							else if (element.attachEvent)
								element.attachEvent('on' + event, func);
							else
								element['on' + event] = func;
						}};

						eventListener(window,'focus',function(){{window.external.DocumentFocused()}});

						eventListener(document, 'click', function (event) {{
							
							var target = event.target;
							if (target.tagName.toLowerCase() !== 'a') return;
							
							var href = target.getAttribute('href');
							if (!href || href[0] !== '#') return;
							
							var htmlExtensionIndex = href.toLowerCase().indexOf(htmlExtension);
							if (htmlExtensionIndex === -1) return;
							
							if (event.preventDefault) event.preventDefault()
							if (event.stopPropagation) event.stopPropagation();
							
							var assemblyAndQualifiedClassName = href.substring(1, htmlExtensionIndex);
							var delimiterIndex = assemblyAndQualifiedClassName.indexOf('{assemblyClassDelimiter}');
							var assembly = assemblyAndQualifiedClassName.substring(0, delimiterIndex);
							var qualifiedClassName = assemblyAndQualifiedClassName.substring(delimiterIndex + 1);
							var fileLine = href.substring(htmlExtensionIndex + htmlExtension.length);
							
							if (fileLine.indexOf('#') !== -1)
								fileLine = fileLine.substring(fileLine.indexOf('#') + 1).replace('file', '').replace('line', '').split('_');
							else
								fileLine = ['-1', '0'];
							
							window.external.OpenFile(assembly, qualifiedClassName, parseInt(fileLine[0]), parseInt(fileLine[1]));
							
							return false;
						}});
							
					</script>
					</body>
				");

				htmlSb.Replace("</head>", $@"
				<style type=""text/css"" id='fccMediaStyle'>
					@media screen and (-ms-high-contrast:active){{
						table.coverage > td.green{{ background-color: windowText }}
						table.coverage > td.gray{{ 
							background-color: {ToJsColour(reportColours.GrayCoverage)}
						}}
	
					}}
				</style>

				</head>
			");

				htmlSb.Replace("<body>", $@"
					<body oncontextmenu='return false;'>
					<style id='fccStyle2'>
						#divHeader {{
							background-color: {ToJsColour(reportColours.DivHeaderBackgroundColour)};
						}}
						table#headerTabs td {{
							color: {ToJsColour(reportColours.HeaderFontColour)};
							border-color: {ToJsColour(reportColours.HeaderBorderColour)};
						}}	
						table#headerTabs td {{
							border-width:3px;
							padding: 3px;
							padding-left: 7px;
							padding-right: 7px;
						}}
						table#headerTabs td.tab {{
							cursor: pointer;
							background-color : {ToJsColour(reportColours.TabBackgroundColour)};
						}}
						table#headerTabs td.active {{
							border-bottom: 3px solid transparent;
							font-weight: bolder;
						}}
					
					</style>
					<script>
						var body = document.getElementsByTagName('body')[0];
						body.style['padding-top'] = '50px';
					
						var tabs = [
							{{ button: 'btnCoverage', content: 'coverage-info' }}, 
							{{ button: 'btnSummary', content: 'table-fixed' }},
							{{ button: 'btnRiskHotspots', content: 'risk-hotspots' }},
						];

						var riskHotspotsTable;
						var riskHotspotsElement;
						var addedFileIndexToRiskHotspots = false;
						var addFileIndexToRiskHotspotsClassLink = function(){{
						  if(!addedFileIndexToRiskHotspots){{
							addedFileIndexToRiskHotspots = true;
							var riskHotspotsElements = document.getElementsByTagName('risk-hotspots');
							if(riskHotspotsElements.length == 1){{
								riskHotspotsElement = riskHotspotsElements[0];
								riskHotspotsTable = riskHotspotsElement.querySelector('table');
								if(riskHotspotsTable){{
									var rhBody = riskHotspotsTable.querySelector('tbody');
									var rows = rhBody.rows;
									for(var i=0;i<rows.length;i++){{
									  var row = rows[i];
									  var cells = row.cells;
									  var classCell = cells[1];
									  var classLink = classCell.children[0];
									  var methodCell = cells[2];
									  var classLink = classCell.children[0];
									  var methodLink = methodCell.children[0];
									  var methodHash = methodLink.hash;
									  var methodHtmlIndex = methodHash.indexOf('.html');
									  var fileLine = methodHash.substring(methodHtmlIndex + 6);
									  var fileAndLine = fileLine.replace('file', '').replace('line', '').split('_');
									  var file = fileAndLine[0];
									  var line = fileAndLine[1];
									  classLink.href = classLink.hash + '#file' + file + '_line0';
									}}
								}}
								
							}}
							}}
						}}
						
						// necessary for WebBrowser 
						function removeElement(element){{
							element.parentNode.removeChild(element);
						}}

						function insertAfter(newNode, existingNode) {{
							existingNode.parentNode.insertBefore(newNode, existingNode.nextSibling);
						}}

						var noHotspotsMessage
						var addNoRiskHotspotsMessageIfRequired = function(){{
							if(riskHotspotsTable == null){{
								noHotspotsMessage = document.createElement(""p"");
								noHotspotsMessage.style.margin = ""0"";
								var header = ""{noRiskHotspotsHeader}"";
								var cyclomaticMessage = ""{noRiskHotspotsCyclomaticMsg}"";
								var crapMessage =""{noRiskHotspotsCrapMessage}""; 
								var nPathMessage = ""{noRiskHotspotsNpathMsg}"";
								noHotspotsMessage.innerText = header + ""\n"" + cyclomaticMessage + ""\n"" + crapMessage + ""\n"" + nPathMessage;

								insertAfter(noHotspotsMessage, riskHotspotsElement);
							}}
						}}

						var removeNoRiskHotspotsMessage = function(){{
							if(noHotspotsMessage){{
								removeElement(noHotspotsMessage);
								noHotspotsMessage = null;
							}}
						}}

						var openTab = function (tabIndex) {{
							if(tabIndex==2){{
								addFileIndexToRiskHotspotsClassLink();
								addNoRiskHotspotsMessageIfRequired();
							}}else{{
								removeNoRiskHotspotsMessage();
							}}
							for (var i = 0; i < tabs.length; i++) {{
							
								var tab = tabs[i];
								if (!tab) continue;
							
								var button = document.getElementById(tab.button);
								if (!button) continue;
							
								var content = document.getElementsByTagName(tab.content)[0];
								if (!content) content = document.getElementsByClassName(tab.content)[0];
								if (!content) continue;
							
								if (i == tabIndex) {{
									if (button.className.indexOf('active') == -1) button.className += ' active';
									content.style.display = 'block';
								}} else {{
									button.className = button.className.replace('active', '');
									content.style.display = 'none';
								}}
							}}
						}};
					
						window.addEventListener('load', function() {{
							openTab(0);
						}});
					
					</script>
					<div id='divHeader' style='border-collapse:collapse;padding:0;padding-top:3px;margin:0;border:0;position:fixed;top:0;left:0;width:100%;z-index:100' cellpadding='0' cellspacing='0'>
						<table id='headerTabs' style='border-collapse:collapse;padding:0;margin:0;border:0' cellpadding='0' cellspacing='0'>
							<tr style='padding:0;margin:0;border:0;'>
								<td style='width:3px;white-space:no-wrap;padding-left:0;padding-right:0;border-left:0;border-top:0'>
								</td>
								<td id='btnCoverage' onclick='return openTab(0);' class='tab' style='width:1%;white-space:no-wrap;'>
									Coverage
								</td>
								<td id='btnSummary' onclick='return openTab(1);' class='tab' style='width:1%;white-space:no-wrap'>
									Summary
								</td>
								<td id='btnRiskHotspots' onclick='return openTab(2);' class='tab' style='width:1%;white-space:no-wrap'>
									Risk Hotspots
								</td>
								<td style='border-top:transparent;border-right:transparent;padding-top:0px' align='center'>
									<a href='#' onclick='return window.external.RateAndReview();' style='margin-right:7px'>Rate & Review</a>
									<a href='#' onclick='return window.external.LogIssueOrSuggestion();' style='margin-left:7px'>Log Issue/Suggestion</a>
								</td>
								<td style='width:1%;white-space:no-wrap;border-top:transparent;border-right:transparent;border-left:transparent;padding-top:0px'>
									<a href='#' onclick='return window.external.BuyMeACoffee();'>Buy me a coffee</a>
								</td>
							</tr>
						</table>
					</div>
				");

                if (!showBranchCoverage)
                {
					htmlSb.Replace("branchCoverageAvailable = true", "branchCoverageAvailable = false");
				}
				
				var processed = string.Join(
				Environment.NewLine,
				htmlSb.ToString().Split('\r', '\n')
				.Select(line =>
				{
					// modify column widths

					if (line.StartsWith(".column"))
					{
						line = $"{line.Substring(0, line.IndexOf('{')).Trim('{')} {{white-space: nowrap; width:1%;}}";
					}

					// modify coverage data

					if (line.IndexOf(@"""name"":") != -1 && line.IndexOf(@"""rp"":") != -1 && line.IndexOf(@"""cl"":") != -1)
					{
						var lineJO = JObject.Parse(line.TrimEnd(','));
						var name = lineJO.Value<string>("name");

						if (name.Equals("AutoGeneratedProgram"))
						{
							// output line

							line = string.Empty;
						}
						else
						{
							// simplify name

							var lastIndexOfDotInName = name.LastIndexOf('.');
							if (lastIndexOfDotInName != -1) lineJO["name"] = name.Substring(lastIndexOfDotInName).Trim('.');

							// prefix the url with #

							lineJO["rp"] = $"#{lineJO.Value<string>("rp")}";

							// output line

							line = $"{lineJO.ToString(Formatting.None)},";
						}
					}

					// modify risk host spots data

					if (line.IndexOf(@"""assembly"":") != -1 && line.IndexOf(@"""class"":") != -1 && line.IndexOf(@"""reportPath"":") != -1)
					{
						var lineJO = JObject.Parse($"{{ {line.TrimEnd(',')} }}");

						// simplify class

						var _class = lineJO.Value<string>("class");
						var lastIndexOfDotInClass = _class.LastIndexOf('.');
						if (lastIndexOfDotInClass != -1) lineJO["class"] = _class.Substring(lastIndexOfDotInClass).Trim('.');

						// prefix the urls with #

						lineJO["reportPath"] = $"#{lineJO.Value<string>("reportPath")}";

						// output line

						line = $"{lineJO.ToString(Formatting.None).Trim('{', '}')},";
					}

					return line;
				}));

				var processedHtmlFile = Path.Combine(reportOutputFolder, "index-processed.html");
				File.WriteAllText(processedHtmlFile, processed);

				return processed;

			});
		}

		private void HideRowsFromOverviewTable(HtmlDocument doc)
        {
			var table = doc.DocumentNode.QuerySelectorAll("table.overview").First();
			var tableRows = table.QuerySelectorAll("tr").ToArray();
			try { tableRows[0].SetAttributeValue("style", "display:none"); } catch { } // generated on
			try { tableRows[1].SetAttributeValue("style", "display:none"); } catch { } // parser
			if (!showBranchCoverage)
			{
				try { tableRows[10].SetAttributeValue("style", "display:none"); } catch { } // covered branches
				try { tableRows[11].SetAttributeValue("style", "display:none"); } catch { } // total branches
				try { tableRows[12].SetAttributeValue("style", "display:none"); } catch { } // branch coverage
			}
		}

		private (int cyclomaticThreshold, int crapScoreThreshold, int nPathThreshold) HotspotThresholds()
        {
			var options = appOptionsProvider.Get();
			return (
				options.ThresholdForCyclomaticComplexity,
				options.ThresholdForCrapScore,
				options.ThresholdForNPathComplexity
			);

		}

		private void ReportColoursProvider_ColoursChanged(object sender, IReportColours reportColours)
		{
			var coverageTableActiveSortColour = reportColours.CoverageTableActiveSortColour;
			var coverageTableExpandCollapseIconColour = reportColours.CoverageTableExpandCollapseIconColour;
			var jsThemeStyling = new JsThemeStyling
			{
				BackgroundColour = ToJsColour(reportColours.BackgroundColour),

				CoverageTableHeaderFontColour = ToJsColour(reportColours.CoverageTableHeaderFontColour),

				DownActiveBase64 = downActiveBase64ReportImage.Base64FromColour(ToJsColour(coverageTableActiveSortColour)),
				UpActiveBase64 = upActiveBase64ReportImage.Base64FromColour(ToJsColour(coverageTableActiveSortColour)),

				DownInactiveBase64 = downInactiveBase64ReportImage.Base64FromColour(ToJsColour(reportColours.CoverageTableInactiveSortColour)),

				MinusBase64 = minusBase64ReportImage.Base64FromColour(ToJsColour(coverageTableExpandCollapseIconColour)),
				PlusBase64 = plusBase64ReportImage.Base64FromColour(ToJsColour(coverageTableExpandCollapseIconColour)),

				CoverageTableRowHoverBackgroundColour = ToJsColour(reportColours.CoverageTableRowHoverBackgroundColour),
				DivHeaderBackgroundColour = ToJsColour(reportColours.DivHeaderBackgroundColour),
				FontColour = ToJsColour(reportColours.FontColour),
				HeaderBorderColour = ToJsColour(reportColours.HeaderBorderColour),
				HeaderFontColour = ToJsColour(reportColours.HeaderFontColour),
				LinkColour = ToJsColour(reportColours.LinkColour),
				TableBorderColour = ToJsColour(reportColours.TableBorderColour),
				TextBoxBorderColour = ToJsColour(reportColours.TextBoxBorderColour),
				TextBoxColour = ToJsColour(reportColours.TextBoxColour),
				TextBoxTextColour = ToJsColour(reportColours.TextBoxTextColour),
				TabBackgroundColour = ToJsColour(reportColours.TabBackgroundColour),

				GrayCoverage = ToJsColour(reportColours.GrayCoverage),

				ComboBox = ToJsColour(reportColours.ComboBoxColour),
				ComboBoxBorder = ToJsColour(reportColours.ComboBoxBorderColour),
				ComboBoxText = ToJsColour(reportColours.ComboBoxTextColour),

				ScrollBarArrow = ToJsColour(reportColours.ScrollBarArrowColour),
				ScrollBarTrack = ToJsColour(reportColours.ScrollBarTrackColour),
				ScrollBarThumb = ToJsColour(reportColours.ScrollBarThumbColour)
			};

			scriptInvoker.InvokeScript(ThemeChangedJSFunctionName, jsThemeStyling);
		}

		private string ToJsColour(System.Drawing.Color colour)
		{
			return $"rgba({colour.R},{colour.G},{colour.B},{colour.A})";
		}

	}
}