﻿using System;
using Nutiteq.Core;
using Nutiteq.Graphics;
using Nutiteq.Ui;
using Nutiteq.Utils;
using Nutiteq.Layers;
using Nutiteq.DataSources;
using Nutiteq.VectorElements;
using Nutiteq.Projections;
using Nutiteq.Styles;
using Nutiteq.PackageManager;
using Nutiteq.WrappedCommons;
using Nutiteq.VectorTiles;
using Newtonsoft.Json.Linq;
using Nutiteq.Geometry;
using System.Threading.Tasks;

namespace NutiteqSample
{
	public class MapSetup
	{
		// Set base projection
		public static EPSG3857 proj = new EPSG3857();

		public static void InitLocation(IMapView mapView){
			
			// Set initial location and other parameters, don't animate
			mapView.FocusPos = proj.FromWgs84(new MapPos(-0.8164,51.2383)); // Berlin
			mapView.Zoom = 2;
			mapView.MapRotation = 0;
			mapView.Tilt = 90;
		}


		public static void InitializePackageManager(string packageFolder, string importPackagePath, IMapView mapView,string downloadedPackage ) {

			// offline base layer

			// 2. define listener, definition is in same class above
			var packageManager = new NutiteqPackageManager("nutiteq.osm", packageFolder);
			packageManager.PackageManagerListener = new PackageListener(packageManager, downloadedPackage);

			// Download new package list only if it is older than 24h
			// Note: this is only needed if pre-made packages are used
			if (packageManager.ServerPackageListAge > 24 * 60 * 60) {
					packageManager.StartPackageListDownload ();
			}

			// start manager - mandatory
			packageManager.Start ();

			// Import initial package
			if (packageManager.GetLocalPackage("world0_4") == null) {
				packageManager.StartPackageImport ("world0_4", 1, importPackagePath);
			}
				
			// Now can add vector map as layer
			// define styling for vector map
			UnsignedCharVector styleBytes = AssetUtils.LoadBytes("nutibright-v2a.zip");
			MBVectorTileDecoder vectorTileDecoder = null;
			if (styleBytes != null) {
				// Create style set
				MBVectorTileStyleSet vectorTileStyleSet = new MBVectorTileStyleSet (styleBytes);
				vectorTileDecoder = new MBVectorTileDecoder (vectorTileStyleSet);
			} else {
				Log.Error ("Failed to load style data");
			}

			// Create online base layer (no package download needed then). Use vector style from assets (osmbright.zip)
			// comment in to use online map. Packagemanager stuff is not needed then
			//			VectorTileLayer baseLayer = new NutiteqOnlineVectorTileLayer("osmbright.zip");

			var baseLayer = new VectorTileLayer(new PackageManagerTileDataSource(packageManager),vectorTileDecoder);
			mapView.Layers.Add(baseLayer);

		}


		public static void StartBboxDownload(NutiteqPackageManager packageManager) {

			// bounding box download can be done now
			// for country package download see OnPackageListUpdated in PackageListener
			String bbox = "bbox(-0.8164,51.2382,0.6406,51.7401)"; // London (about 30MB)
			if (packageManager.GetLocalPackage(bbox) == null) {
				packageManager.StartPackageDownload (bbox);
			}
		}


		async public static void AddMapOverlays(IMapView mapView) {

			// Create overlay layer for markers
			var dataSource = new LocalVectorDataSource (proj);
			var overlayLayer = new VectorLayer (dataSource);
			mapView.Layers.Add (overlayLayer);

			// Create line style, and line poses
			var lineStyleBuilder = new LineStyleBuilder();
			lineStyleBuilder.LineJointType = LineJointType.LineJointTypeRound;
			lineStyleBuilder.Width = 8;

			var linePoses = new MapPosVector ();
			linePoses.Add(proj.FromWgs84(new MapPos(0, 0)));
			linePoses.Add(proj.FromWgs84(new MapPos(0, 80)));
			linePoses.Add(proj.FromWgs84(new MapPos(45, 45)));
			var line = new Line (linePoses, lineStyleBuilder.BuildStyle ());
			dataSource.Add (line);

			// Create balloon popup
			var balloonPopupStyleBuilder = new BalloonPopupStyleBuilder();
			balloonPopupStyleBuilder.CornerRadius = 3;
			balloonPopupStyleBuilder.TitleFontName = "Helvetica";
			balloonPopupStyleBuilder.TitleFontSize = 55;
			balloonPopupStyleBuilder.TitleColor = new Color(200,0,0,255);
			balloonPopupStyleBuilder.StrokeColor = new Color(200, 120, 0, 255);
			balloonPopupStyleBuilder.StrokeWidth = 1;
			balloonPopupStyleBuilder.PlacementPriority = 1;
			var popup = new BalloonPopup(
				proj.FromWgs84(new MapPos(0, 20)),
				balloonPopupStyleBuilder.BuildStyle(),
				"Title", "Description");
			dataSource.Add(popup);

			// load NML file model from a file
			var modelFile = AssetUtils.LoadBytes("fcd_auto.nml");

			// set location for model, and create NMLModel object with this
			var modelPos = proj.FromWgs84(new MapPos(24.646469, 59.423939));
			var model = new NMLModel(modelPos, modelFile);
			mapView.FocusPos = modelPos;
			mapView.Zoom = 15;

			// oversize it 20*, just to make it more visible (optional)
			model.Scale = 20;

			// add metadata for click handling (optional)
			model.SetMetaDataElement("ClickText", "Single model");

			// add it to normal datasource
			dataSource.Add(model);

			// Create and set map listener
			mapView.MapEventListener = new MapListener (dataSource);

			await AnimateModel (model);

		}

		public static async Task AnimateModel(NMLModel model){
			for (int i = 0; i < 3600; i++) { 
					model.SetRotation (new MapVec (0, 0, 1), i);
				await Task.Delay(10);
			}
		}


		public static void addJosnLayer(IMapView mapView, String json){

			var features = Newtonsoft.Json.Linq.JObject.Parse (json)["features"];

			var geoJsonParser = new GeoJSONGeometryReader();

			var proj = new EPSG3857 ();
			var balloonPopupStyleBuilder = new BalloonPopupStyleBuilder();

			// Create overlay layer for markers
			var dataSource = new LocalVectorDataSource (proj);
			var overlayLayer = new ClusteredVectorLayer (dataSource, new MyClusterElementBuilder());
			overlayLayer.MinimumClusterDistance = 80; // in pixels
			mapView.Layers.Add (overlayLayer);

			foreach(var feature in features){
				var featureType = feature ["type"];

				var geometry = feature ["geometry"];
				var ntGeom = geoJsonParser.ReadGeometry (Newtonsoft.Json.JsonConvert.SerializeObject(geometry));

				var popup = new BalloonPopup(
					ntGeom,
					balloonPopupStyleBuilder.BuildStyle(),
					(string) feature ["properties"]["Capital"], (string) feature ["properties"]["Country"]);

				var properties = (JObject) feature ["properties"];
				foreach (var property in properties) {
					var key = (string) property.Key;
					var value = (string) property.Value;
					popup.SetMetaDataElement(key,value);
				}

				dataSource.Add (popup);

			}

		}

	}

	class MyClusterElementBuilder : ClusterElementBuilder
	{
		BalloonPopupStyleBuilder balloonPopupStyleBuilder;

		public MyClusterElementBuilder() {
			balloonPopupStyleBuilder = new BalloonPopupStyleBuilder();
			balloonPopupStyleBuilder.CornerRadius = 3;
			balloonPopupStyleBuilder.TitleMargins = new BalloonPopupMargins (6, 6, 6, 6);
			balloonPopupStyleBuilder.LeftColor = new Color(240,230,140,255);
		}

		public override VectorElement BuildClusterElement(MapPos pos, VectorElementVector elements) {
			var popup = new BalloonPopup(
				pos,
				balloonPopupStyleBuilder.BuildStyle(),
				elements.Count.ToString(), "");
			return popup;
		}

	}



}


