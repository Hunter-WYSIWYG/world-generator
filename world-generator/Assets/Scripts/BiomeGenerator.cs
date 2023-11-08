using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

//credit for voronoi generator: UpGames: "Voronoi diagram tutorial in Unity3D(C#)" (https://www.youtube.com/watch?v=EDv69onIETk)
public class BiomeGenerator : MonoBehaviour
{
	public bool generateNewMap = true;
	[Header("Local Minimum Centroids")]
	public int localMinimumArea;
	public bool useMinimaForCentroids;
	public bool showLocalMinima;
	[Header("Random Centroids")]
	public int regionAmount;
	[Header("Map Parameter")]
	[Range(0,1)]
	public float averageTemp = 0.5f;
	[Range(0,1)]
	public float averagePrec = 0.5f;
	public float minkowskiLambda = 3f;
	//upper x percent of terrain that have a colder temperature
	[Range(0f,1f)]
	public float realitveColdHeightsLimit = 0f;
	//maximum distance in mesh-nodes around waters that is still affected by higher humidity
	public int humiditySpread = 0;
	[Header("Sprite Processing")]
	[Range(0f,1f)]
	//eliminate regions that have a smaller size than width*height*eliminationSizeFactor
	public float eliminationSizeFactor = 0.001f;
	public bool biome_eliminateSmallRegions = false;
	public bool biome_openSprite = false;
	public bool biome_mergeTempAndPrec = false;

	[Header("Texture Blending Settings")]
	//thickness of the blending border of each area in Pixel
    public int textureBlendingArea;
    //color intensity of blending areas
    public float blendGradationFactor;
    public float fadingOutFactor;

	public enum WizardState {BiomeView, TempView, PrecView, TerrainView, PlayerView};
	[Header("View State")]
	public WizardState wizardState = WizardState.TempView;
	[Header("Dependencies")]
	public TerrainGenerator terrainGenerator;
	public GameObject tempSprite;
	public GameObject precSprite;
	public GameObject biomeSprite;
	public GameObject spriteCam;
	public GameObject terrainCam;
	public GameObject playerCam;
	public SpriteToScreenSize biomeMapView;
	public SpriteToScreenSize tempMapView;
	public SpriteToScreenSize precMapView;
	public Texture2D tempTexture;
	public Texture2D precTexture;
	private Texture2D biomeTexture;
	private int[] tempIntensityArray;
	private int[] precIntensityArray;
	private bool biomeGenerationFinished = false;
	//contains a list of region neighbor ids for every region
	private List<int>[] tempRegionNeighbors;
	private List<int>[] precRegionNeighbors;
	private Texture2D steepnessMap;
	enum ColorChannel {R,G,B,A};

	//Temp (0-2) & Prec (3-5) Colors
	private static Dictionary<int, Color> tempColors = new Dictionary<int, Color>() {
		{0, new Color(0.9f, 0.9f, 0, 1)},
		{1, new Color(1, 0.4f, 0, 1)},
		{2, new Color(0.9f, 0, 0, 1)}
	};
	private static Dictionary<int, Color> precColors = new Dictionary<int, Color>() {
		{0, new Color(0.1f, 1, 0.9f, 1)},
		{1, new Color(0.1f, 0.6f, 1, 1)},
		{2, new Color(0, 0.1f, 0.9f, 1)}
	};

	private static Dictionary<int, Color> biomeColors = new Dictionary<int, Color>() {
		{0, mergeColors(tempColors[0], precColors[0], 0.5f)},
		{1, mergeColors(tempColors[0], precColors[1], 0.5f)},
		{2, mergeColors(tempColors[0], precColors[2], 0.5f)},
		{3, mergeColors(tempColors[1], precColors[0], 0.5f)},
		{4, mergeColors(tempColors[1], precColors[1], 0.5f)},
		{5, mergeColors(tempColors[1], precColors[2], 0.5f)},
		{6, mergeColors(tempColors[2], precColors[0], 0.5f)},
		{7, mergeColors(tempColors[2], precColors[1], 0.5f)},
		{8, mergeColors(tempColors[2], precColors[2], 0.5f)},
	};

	private Vector2Int textureSize;
	private WizardState currentWizardState;
	private List<Vector2Int> localMinima;
	private Texture2D biomeSplatmap_temp0;
	private Texture2D biomeSplatmap_temp1;
	private Texture2D biomeSplatmap_temp2;

	void Start() {
		generateNewMap = true;
		currentWizardState = WizardState.TerrainView;
		wizardState = WizardState.BiomeView;
		localMinima = new List<Vector2Int>();
	}

	private void Update() {
		if(generateNewMap && terrainGenerator.isMeshGenerated()) {
			textureSize = terrainGenerator.getUncondensedMeshSize();
			generateNewMap = false;
			localMinima = findLocalMinima(terrainGenerator.getUncondensedTerrainVertices(), localMinimumArea);
			if(textureSize.x * textureSize.y > 0) {
				int centroidCount;
				if(useMinimaForCentroids) {
					centroidCount = localMinima.Count;
				} else {
					centroidCount = regionAmount;
				}
				
				//init region neighbor arrays
				tempRegionNeighbors = new List<int>[centroidCount];
				for (int i = 0; i < centroidCount; i++) {
					tempRegionNeighbors[i] = new List<int>();
				}
				precRegionNeighbors = new List<int>[centroidCount];
				for (int i = 0; i < centroidCount; i++) {
					precRegionNeighbors[i] = new List<int>();
				}


				//generate/set intensityArrays, textures, sprites
				tempIntensityArray = calculateVoronoiDiagram(true);
				tempIntensityArray = addMountainTemperatures(tempIntensityArray);
				tempTexture = GetTextureFromIntensityArray(tempIntensityArray, tempColors);
				tempSprite.GetComponent<SpriteRenderer>().sprite = buildSprite(tempTexture);

				precIntensityArray = calculateVoronoiDiagram(false);
				precIntensityArray = addWaterHumidity(precIntensityArray);
				precTexture = GetTextureFromIntensityArray(precIntensityArray, precColors);
				precSprite.GetComponent<SpriteRenderer>().sprite = buildSprite(precTexture);

				biomeTexture = MergeTextures(tempTexture, precTexture);
				biomeSprite.GetComponent<SpriteRenderer>().sprite = buildSprite(biomeTexture);
				biomeGenerationFinished = true;

				if(wizardState != WizardState.PlayerView) {
					biomeMapView.resize();
					tempMapView.resize();
					precMapView.resize();
				}

				recalculateTextures();
			} else {
				Debug.Log("Texture size too small");
			}
		}

		if(biomeGenerationFinished) {
			if(biome_mergeTempAndPrec) {
				biome_mergeTempAndPrec = false;
				biomeTexture = MergeTextures(tempTexture, precTexture);
				biomeSprite.GetComponent<SpriteRenderer>().sprite = buildSprite(biomeTexture);
				biomeSprite.GetComponent<FreeDraw.Drawable>().init();
				recalculateTextures();
			}

			if(biome_eliminateSmallRegions) {
				biome_eliminateSmallRegions = false;
				biomeTexture = EliminateSmallRegions(biomeTexture, eliminationSizeFactor);
				biomeSprite.GetComponent<SpriteRenderer>().sprite = buildSprite(biomeTexture);
				biomeSprite.GetComponent<FreeDraw.Drawable>().init();
				recalculateTextures();
			}

			if(biome_openSprite) {
				biome_openSprite = false;
				List<Color> currentBiomeColors = determineBiomeColors(biomeTexture);
				biomeTexture = openTexture(currentBiomeColors, biomeTexture);
				biomeSprite.GetComponent<SpriteRenderer>().sprite = buildSprite(biomeTexture);
				biomeSprite.GetComponent<FreeDraw.Drawable>().init();
				recalculateTextures();
			}

			controlSpriteWizard();
		}
	}

	void recalculateTextures() {
		biomeSplatmap_temp0 = biomeToSplatmap(biomeTexture, biomeColors[0], biomeColors[1], biomeColors[2]);
		biomeSplatmap_temp1 = biomeToSplatmap(biomeTexture, biomeColors[3], biomeColors[4], biomeColors[5]);
		biomeSplatmap_temp2 = biomeToSplatmap(biomeTexture, biomeColors[6], biomeColors[7], biomeColors[8]);
		
		biomeSplatmap_temp0 = addBlendingArea(biomeSplatmap_temp0, textureBlendingArea, blendGradationFactor, fadingOutFactor);
		biomeSplatmap_temp1 = addBlendingArea(biomeSplatmap_temp1, textureBlendingArea, blendGradationFactor, fadingOutFactor);
		biomeSplatmap_temp2 = addBlendingArea(biomeSplatmap_temp2, textureBlendingArea, blendGradationFactor, fadingOutFactor);
		
		SaveTextureAsPNG(biomeSplatmap_temp0, "Assets/Textures/biomeSplatmap_temp0.png");
		SaveTextureAsPNG(biomeSplatmap_temp1, "Assets/Textures/biomeSplatmap_temp1.png");
		SaveTextureAsPNG(biomeSplatmap_temp2, "Assets/Textures/biomeSplatmap_temp2.png");

		steepnessMap = calcSteepnessTexture(textureSize, terrainGenerator.getUncondensedTerrainVertices());
		SaveTextureAsPNG(steepnessMap, "Assets/Textures/steepnessMap.png");
		
		AssetDatabase.Refresh();
	}

	//output meaning: 0f = 0°; 1f = 90° steepness
	private Texture2D calcSteepnessTexture(Vector2Int meshSize, Vector3[] uncondensedTerrainVertices) {
		Texture2D steepnessTex = new Texture2D(meshSize.x, meshSize.y);
		steepnessTex.filterMode = FilterMode.Point;
		for(int y = 0; y < meshSize.y; y++) {
			for(int x = 0; x < meshSize.x; x++) {
				float steepness = calcSteepness(new Vector2Int(x, y));
				steepnessTex.SetPixel(x, y, new Color(steepness, 0, 0, 1));
			}
		}
		return steepnessTex;
		
		float calcSteepness(Vector2Int position) {
			float slopeX = getHeight(position.x <  meshSize.x - 1 ? position.x + 1: position.x, position.y) - getHeight(position.x > 0 ? position.x - 1 :  position.x, position.y);
			float slopeZ = getHeight(position.x, position.y <  meshSize.y- 1 ? position.y + 1: position.y) - getHeight(position.x, position.y > 0 ? position.y - 1 :  position.y);
			
			if (position.x == 0 || position.x == meshSize.x - 1)
				slopeX *= 2;
			if (position.y == 0 || position.y == meshSize.y - 1)
				slopeZ *= 2;
			
			Vector3 normal = new Vector3(-slopeX, 1, slopeZ);
			normal.Normalize();
			float steepness = Mathf.Acos(Vector3.Dot(normal, Vector3.up));
			return Mathf.InverseLerp(0f, 1.6f, steepness);
		}

		float getHeight(int x, int y) {
			int index = meshSize.x * y + x;
			return uncondensedTerrainVertices[index].y/terrainGenerator.terrainScaling;
		}
	}

	Texture2D addBlendingArea(Texture2D splatmap, int textureBlendingArea, float blendGradationFactor, float fadingOutFactor) {
		//the gradation steps in which the color fades out
		float blendGradation = 1f/(textureBlendingArea*blendGradationFactor);

		Texture2D iterationResultTex = copyTexture(splatmap);
		Vector2Int[] eightNeighborhoodCoords = {
			new Vector2Int(1,1),
			new Vector2Int(1,0),
			new Vector2Int(1,-1),
			new Vector2Int(0,-1),
			new Vector2Int(-1,-1),
			new Vector2Int(-1,0),
			new Vector2Int(-1,1),
			new Vector2Int(0,1)
		};
		ColorChannel[] colorsToEdit = {
			ColorChannel.R,
			ColorChannel.G,
			ColorChannel.B
		};

		//for every color to edit
		foreach(ColorChannel color in colorsToEdit) {

			//for every row of blending area
			for(int i = 0; i < textureBlendingArea; i++) {

				//for every pixel
				for(int y = 0; y < splatmap.height; y++) {
					for(int x = 0; x < splatmap.width; x++) {
						if(i < textureBlendingArea/2) {
							fadeIn(x, y, color);
						} else {
							fadeOut(x, y, color, i);
						}
					}
				}
				splatmap = copyTexture(iterationResultTex);
			}
		}
		return splatmap;

		//fade-in color from area border to inner area
		void fadeIn(int x, int y, ColorChannel colorChannel) {
			float currentColorValue = getValueFromColor(splatmap.GetPixel(x,y), colorChannel);
			if(currentColorValue == 1) {
				foreach(Vector2Int coords in eightNeighborhoodCoords) {
					if(checkNeighborAndRecolor(new Vector2Int(x, y), new Vector2Int(x + coords.x, y + coords.y))) {
						break;
					}
				}

				bool checkNeighborAndRecolor(Vector2Int currentPixel, Vector2Int neighborPixel) {
					Color neighborColor = splatmap.GetPixel(neighborPixel.x, neighborPixel.y);
					Color currentColor = splatmap.GetPixel(currentPixel.x, currentPixel.y);
					float neighborColorValue = getValueFromColor(neighborColor, colorChannel);

					if(isPixelInTexture(neighborPixel, new Vector2Int(splatmap.width, splatmap.height)) && neighborColorValue < 1) {
						float newColorValue = Mathf.Max(neighborColorValue + blendGradation, 1 - (blendGradation * Mathf.Floor(textureBlendingArea/2)));
						newColorValue = Mathf.Min(1f, newColorValue);
						newColorValue = Mathf.Round(newColorValue * 100)/100;

						Color newColor = getColorFromValue(currentColor, newColorValue, colorChannel);
						iterationResultTex.SetPixel(currentPixel.x,currentPixel.y,newColor);
						return true;
					}
					return false;
				}
			}
		}

		//fade-out color from area border to outer area
		void fadeOut(int x, int y, ColorChannel colorChannel, int iteration) {
			float currentColorValue = getValueFromColor(splatmap.GetPixel(x,y), colorChannel);
			if(currentColorValue > 0) {
				float newColorValue_fadingOut = Mathf.Max(0f, currentColorValue - (blendGradation + blendGradation*fadingOutFactor*iteration));
				newColorValue_fadingOut = Mathf.Round(newColorValue_fadingOut * 100)/100;

				foreach(Vector2Int coords in eightNeighborhoodCoords) {
					checkNeighborAndRecolor(newColorValue_fadingOut, new Vector2Int(x + coords.x, y + coords.y));
				}

				void checkNeighborAndRecolor(float newColorValue, Vector2Int pixelCoords) {
					Color currentColor = splatmap.GetPixel(pixelCoords.x, pixelCoords.y);
					float currentColorValue = getValueFromColor(currentColor, colorChannel);
					if(isPixelInTexture(pixelCoords, new Vector2Int(splatmap.width, splatmap.height)) && currentColorValue <= 0) {
						Color newColor = getColorFromValue(currentColor, newColorValue, colorChannel);
						iterationResultTex.SetPixel(pixelCoords.x,pixelCoords.y,newColor);
					}
				}
			}
		}

		float getValueFromColor(Color color, ColorChannel colorChannel) {
			switch (colorChannel) {
				case ColorChannel.R:
					return color.r;
				case ColorChannel.G:
					return color.g;
				case ColorChannel.B:
					return color.b;
				default:
					return color.a;
			}
		}

		Color getColorFromValue(Color oldColor, float newValue, ColorChannel colorChannel) {
			switch (colorChannel) {
				case ColorChannel.R:
					return new Color(newValue, oldColor.g, oldColor.b, oldColor.a);
				case ColorChannel.G:
					return new Color(oldColor.r, newValue, oldColor.b, oldColor.a);
				case ColorChannel.B:
					return new Color(oldColor.r, oldColor.g, newValue, oldColor.a);
				default:
					return new Color(oldColor.r, oldColor.g, oldColor.b, newValue);
			}
		}
	}

	Texture2D biomeToSplatmap(Texture2D biomeTex, Color biomeColor1, Color biomeColor2, Color biomeColor3) {
		Texture2D resultTexture = new Texture2D(biomeTex.width, biomeTex.height);
		resultTexture.filterMode = FilterMode.Point;
		for(int y = 0; y < biomeTex.height; y++) {
			for(int x = 0; x < biomeTex.width; x++) {
				Color currentBiomeColor = roundColor(biomeTex.GetPixel(x,y),2);
				if(currentBiomeColor == biomeColor1) {
					resultTexture.SetPixel(x,y,Color.red);
				} else if (currentBiomeColor == biomeColor2) {
					resultTexture.SetPixel(x,y,Color.green);
				} else if (currentBiomeColor == biomeColor3) {
					resultTexture.SetPixel(x,y,Color.blue);
				} else {
					resultTexture.SetPixel(x,y,Color.black);
				}
			}
		}
		return resultTexture;
	}

	//setting and getting pixel colors of textures2d produces very small rounding errors
	//in these cases the colors can not be correctly compared anymore
	//this function is used as a work around
	Color roundColor(Color color, int decimals) {
		float decimalsFactor = Mathf.Pow(10, decimals);
		float r = Mathf.Round(color.r * decimalsFactor)/decimalsFactor;
		float g = Mathf.Round(color.g * decimalsFactor)/decimalsFactor;
		float b = Mathf.Round(color.b * decimalsFactor)/decimalsFactor;
		float a = Mathf.Round(color.a * decimalsFactor)/decimalsFactor;
		return new Color(r,g,b,a);
	}

	public static void SaveTextureAsPNG(Texture2D _texture, string _fullPath) {
		byte[] _bytes =_texture.EncodeToPNG();
		System.IO.File.WriteAllBytes(_fullPath, _bytes);
	}

	List<Color> determineBiomeColors(Texture2D biomeTexture) {
		List<Color> biomeColors = new List<Color>();
		for(int y = 0; y < biomeTexture.height; y++) {
			for(int x = 0; x < biomeTexture.width; x++) {
				Color pixelColor = biomeTexture.GetPixel(x,y);
				if(!biomeColors.Contains(pixelColor)) {
					biomeColors.Add(pixelColor);
				}
			}
		}
		return biomeColors;
	}

	Texture2D copyTexture(Texture2D texture) {
		Texture2D copy = new Texture2D(texture.width, texture.height);
		copy.filterMode = FilterMode.Point;
		for(int y = 0; y < texture.height; y++) {
			for(int x = 0; x < texture.width; x++) {
				copy.SetPixel(x,y,texture.GetPixel(x,y));
			}
		}
		return copy;
	}

	Texture2D openTexture(List<Color> imageColors, Texture2D texture) {
		foreach(Color color in imageColors) {
			texture = erodeTexture(color, texture);
			texture = dilateTexture(color, texture);
		}
		return texture;
	}

	Texture2D erodeTexture(Color erosionColor, Texture2D texture) {
		Texture2D resultTexture = copyTexture(texture);
		
		for(int y = 0; y < textureSize.y; y++) {
			for(int x = 0; x < textureSize.x; x++) {
				if(texture.GetPixel(x,y) == erosionColor) {
					if(x-1 >= 0 && texture.GetPixel(x-1,y) != erosionColor) {
						resultTexture.SetPixel(x,y,texture.GetPixel(x-1,y));
					} else if(x+1 < textureSize.x && texture.GetPixel(x+1,y) != erosionColor) {
						resultTexture.SetPixel(x,y,texture.GetPixel(x+1,y));
					} else if(y-1 >= 0 && texture.GetPixel(x,y-1) != erosionColor) {
						resultTexture.SetPixel(x,y,texture.GetPixel(x,y-1));
					} else if(y+1 < textureSize.y && texture.GetPixel(x,y+1) != erosionColor) {
						resultTexture.SetPixel(x,y,texture.GetPixel(x,y+1));
					}
				}
			}
		}
		resultTexture.Apply();
		return resultTexture;
	}

	Texture2D dilateTexture(Color dilatationColor, Texture2D texture) {
		Texture2D resultTexture = copyTexture(texture);

		for(int y = 0; y < textureSize.y; y++) {
			for(int x = 0; x < textureSize.x; x++) {
				if(texture.GetPixel(x,y) != dilatationColor) {
					if(x-1 > 0 && texture.GetPixel(x-1,y) == dilatationColor) {
						resultTexture.SetPixel(x,y,dilatationColor);
					} else if(x+1 < textureSize.x && texture.GetPixel(x+1,y) == dilatationColor) {
						resultTexture.SetPixel(x,y,dilatationColor);
					} else if(y-1 > 0 && texture.GetPixel(x,y-1) == dilatationColor) {
						resultTexture.SetPixel(x,y,dilatationColor);
					} else if(y+1 < textureSize.y && texture.GetPixel(x,y+1) == dilatationColor) {
						resultTexture.SetPixel(x,y,dilatationColor);
					}
				}
			}
		}
		resultTexture.Apply();
		return resultTexture;
	}

	void controlSpriteWizard() {
		if(wizardState != currentWizardState) {
			currentWizardState = wizardState;
			switch (wizardState) {
				case WizardState.TempView:
					terrainCam.SetActive(false);
					spriteCam.SetActive(true);
					playerCam.SetActive(false);

					tempSprite.SetActive(true);
					precSprite.SetActive(false);
					biomeSprite.SetActive(false);
					tempSprite.GetComponent<FreeDraw.Drawable>().setPenColor(tempColors[0]);
					break;
				case WizardState.PrecView:
					terrainCam.SetActive(false);
					spriteCam.SetActive(true);
					playerCam.SetActive(false);

					tempSprite.SetActive(false);
					precSprite.SetActive(true);
					biomeSprite.SetActive(false);
					precSprite.GetComponent<FreeDraw.Drawable>().setPenColor(precColors[0]);
					break;
				case WizardState.BiomeView:
					terrainCam.SetActive(false);
					spriteCam.SetActive(true);
					playerCam.SetActive(false);

					tempSprite.SetActive(false);
					precSprite.SetActive(false);
					biomeSprite.SetActive(true);
					biomeSprite.GetComponent<FreeDraw.Drawable>().setPenColor(biomeTexture.GetPixel(0,0));
					break;
				case WizardState.TerrainView:
					spriteCam.SetActive(false);
					terrainCam.SetActive(true);
					playerCam.SetActive(false);

					tempSprite.SetActive(false);
					precSprite.SetActive(false);
					biomeSprite.SetActive(false);
					break;
				case WizardState.PlayerView:
					spriteCam.SetActive(false);
					terrainCam.SetActive(false);
					playerCam.SetActive(true);

					tempSprite.SetActive(false);
					precSprite.SetActive(false);
					biomeSprite.SetActive(false);
					break;

			}
			if(wizardState != WizardState.PlayerView) {
				biomeMapView.resize();
				tempMapView.resize();
				precMapView.resize();
			}
		}
	}

	int[] addMountainTemperatures(int[] intensityArray) {
		Vector3[] terrainVertices = terrainGenerator.getUncondensedTerrainVertices();
		float maxHeight = terrainGenerator.getMaxTerrainheight();
		float minHeight = terrainGenerator.getMinTerrainheight();

		float coldHeightsLimit = maxHeight - ((maxHeight - minHeight) * realitveColdHeightsLimit);

		for(int y = 0; y < textureSize.y; y++) {
			for(int x = 0; x < textureSize.x; x++) {
				int meshIndex = x + y * textureSize.x;
				if(x < textureSize.x && y < textureSize.y && terrainVertices[meshIndex].y > coldHeightsLimit) {
					int textureX = meshIndex % textureSize.x;
					int textureY = Mathf.FloorToInt(meshIndex / textureSize.x);
					int textureIndex = textureX + textureY * textureSize.x;
					intensityArray[textureIndex] = Mathf.Max(intensityArray[textureIndex] - 1, 0);
				}
			}
		}
		return intensityArray;
	}

	int[] addWaterHumidity(int[] intensityArray) {
		Vector3[] terrainVertices = terrainGenerator.getUncondensedTerrainVertices();
		float maxHeight = terrainGenerator.getMaxTerrainheight();
		float minHeight = terrainGenerator.getMinTerrainheight();
		float relativeWaterHeight = terrainGenerator.relativeWaterHeight;

		float absoluteWaterHeight = Mathf.Lerp(minHeight, maxHeight, relativeWaterHeight);

		//determine all mesh-nodes under water
		bool[] isWaterNode = new bool[terrainVertices.Length];
		for(int y = 0; y < textureSize.y; y++) {
			for(int x = 0; x < textureSize.x; x++) {
				int meshIndex = x + y * textureSize.x;
				if(x < textureSize.x && y < textureSize.y && terrainVertices[meshIndex].y < absoluteWaterHeight) {
					isWaterNode[meshIndex] = true;
				} else {
					isWaterNode[meshIndex] = false;
				}
			}
		}

		//determine mesh-nodes in area around water
		//dilate water formations humiditySpread-Times
		for(int i = 0; i < humiditySpread; i++) {
			isWaterNode = dilateWaterNodes(isWaterNode);
		}

		//add intensity values
		for(int meshY = 0; meshY < textureSize.y; meshY++) {
			for(int meshX = 0; meshX < textureSize.x; meshX++) {
				int meshIndex = meshX + meshY * textureSize.x;
				if(isWaterNode[meshIndex]) {
					int textureX = meshIndex % textureSize.x;
					int textureY = Mathf.FloorToInt(meshIndex / textureSize.x);
					int textureIndex = textureX + textureY * textureSize.x;
					intensityArray[textureIndex] = Mathf.Min(intensityArray[textureIndex] + 1, 2);
				}
			}
		}
		return intensityArray;

		bool[] dilateWaterNodes(bool[] waterNodes) {
			bool[] resultArray = (bool[])waterNodes.Clone();

			for(int meshY = 0; meshY < textureSize.y; meshY++) {
				for(int meshX = 0; meshX < textureSize.x; meshX++) {
					int meshIndex = meshX + meshY * textureSize.x;
					int meshIndexUp = meshX + (meshY+1) * textureSize.x;
					int meshIndexRight = (meshX+1) + meshY * textureSize.x;
					int meshIndexDown = meshX + (meshY-1) * textureSize.x;
					int meshIndexLeft = (meshX-1) + meshY * textureSize.x;
					if(!waterNodes[meshIndex]) {
						if(meshX-1 > 1 && waterNodes[meshIndexLeft]) {
							resultArray[meshIndex] = true;
						} else if(meshX+1 < textureSize.x-1 && waterNodes[meshIndexRight]) {
							resultArray[meshIndex] = true;
						} else if(meshY-1 > 1 && waterNodes[meshIndexDown]) {
							resultArray[meshIndex] = true;
						} else if(meshY+1 < textureSize.y-1 && waterNodes[meshIndexUp]) {
							resultArray[meshIndex] = true;
						}
					}
				}
			}
			return resultArray;
		}
	}

	private Texture2D EliminateSmallRegions(Texture2D texture, float eliminationSizeFactor) {
		Dictionary<Vector2Int, int> pixelRegions = new Dictionary<Vector2Int, int>();
		
		//breath first search -> save sizes of all regions
		int regionCount = 0;
		Dictionary<int, int> regionSizes = new Dictionary<int, int>(); //regionID, regionSize
		for(int x = 0; x < textureSize.x; x++) {
			for(int y = 0; y < textureSize.y; y++) {
				Vector2Int startingPixel = new Vector2Int(x,y);
				if(!pixelRegions.ContainsKey(startingPixel)) {
					regionSizes.Add(regionCount, 0);
					List<Vector2Int> pixelsToCheck = new List<Vector2Int>();
					pixelsToCheck.Add(startingPixel);

					while(pixelsToCheck.Count > 0) {
						Vector2Int currentPixel = pixelsToCheck[0];
						pixelsToCheck.RemoveAt(0);
						if(isPixelInTexture(currentPixel, textureSize) && isPixelColorEqual(startingPixel, currentPixel) && !pixelRegions.ContainsKey(currentPixel)) {
							pixelRegions.Add(currentPixel, regionCount);
							regionSizes[regionCount]++;
							pixelsToCheck.Add(new Vector2Int(currentPixel.x+1, currentPixel.y));
							pixelsToCheck.Add(new Vector2Int(currentPixel.x-1, currentPixel.y));
							pixelsToCheck.Add(new Vector2Int(currentPixel.x, currentPixel.y+1));
							pixelsToCheck.Add(new Vector2Int(currentPixel.x, currentPixel.y-1));
						}
					}
					regionCount++;
				}
			}
		}

		//find biggest and second biggest region of all regions
		Vector2Int biggestRegion = new Vector2Int(-1,0); //regionID, regionSize
		Vector2Int secondBiggestRegion = new Vector2Int(-1,0); //regionID, regionSize
		foreach(KeyValuePair<int, int> regionSize in regionSizes) {
			if(regionSize.Value > biggestRegion.y) {
				secondBiggestRegion = biggestRegion;
				biggestRegion.x = regionSize.Key;
				biggestRegion.y = regionSize.Value;
			} else if(regionSize.Value > secondBiggestRegion.y) {
				secondBiggestRegion.x = regionSize.Key;
				secondBiggestRegion.y = regionSize.Value;
			}
		}

		//detemine minimum size for regions to not get eliminated
		//always let at least the biggest 2 regions exist
		int secondBiggestRegionSize = secondBiggestRegion.y;
		int minRegionSize = Mathf.Min(Mathf.RoundToInt(textureSize.x * textureSize.y * eliminationSizeFactor),secondBiggestRegionSize);

		//list which regions should be eliminated by ID
		List<int> regionsToEliminate = new List<int>();
		for(int i = 0; i < regionSizes.Count; i++) {
			if(regionSizes[i] < minRegionSize) {
				regionsToEliminate.Add(i);
			}
		}

		//eliminate chosen regions by recoloring them
		Color[] resultColorArray = new Color [textureSize.x * textureSize.y];
		for(int y = 0; y < textureSize.y; y++) {
			for(int x = 0; x < textureSize.x; x++) {
				Vector2Int pixel = new Vector2Int(x,y);
				int pixelRegion = pixelRegions[pixel];
				if(regionsToEliminate.Contains(pixelRegion)) {
					resultColorArray[y*textureSize.x + x] = determineNewPixelColor(pixel);
				} else {
					resultColorArray[y*textureSize.x + x] = texture.GetPixel(pixel.x, pixel.y);
				}
			}
		}

		//end of function
		return GetTextureFromColorArray(resultColorArray);


		//some helper functions:

		//get nearest pixelcolor in neighborhood that should not be removed
		Color determineNewPixelColor(Vector2Int pixel) {
			int maxDistanceFromPixel = Mathf.Max(textureSize.x, textureSize.y);
			Vector2Int upperPixel = new Vector2Int(pixel.x, pixel.y+1);
			Vector2Int rightPixel = new Vector2Int(pixel.x+1, pixel.y);
			Vector2Int lowerPixel = new Vector2Int(pixel.x, pixel.y-1);
			Vector2Int leftPixel = new Vector2Int(pixel.x-1, pixel.y);

			//check closest (distance 1) in 4er neighborhood
			if(hasPossibleColor(upperPixel))
				return texture.GetPixel(upperPixel.x, upperPixel.y);
			if(hasPossibleColor(rightPixel))
				return texture.GetPixel(rightPixel.x, rightPixel.y);
			if(hasPossibleColor(lowerPixel))
				return texture.GetPixel(lowerPixel.x, lowerPixel.y);
			if(hasPossibleColor(leftPixel))
				return texture.GetPixel(leftPixel.x, leftPixel.y);

			//check rest in growing circles around pixel
			for(int distance = 2; distance < maxDistanceFromPixel; distance++) {
				for(int xOffset = -distance; xOffset <= distance; xOffset++) {
					Vector2Int pixelToCheck = new Vector2Int(pixel.x + xOffset, pixel.y + distance);
					if(hasPossibleColor(pixelToCheck))
						return texture.GetPixel(pixelToCheck.x, pixelToCheck.y);
				}
				for(int yOffset = 1-distance; yOffset <= -1+distance; yOffset++) {
					Vector2Int pixelToCheck = new Vector2Int(pixel.x + distance, pixel.y + yOffset);
					if(hasPossibleColor(pixelToCheck))
						return texture.GetPixel(pixelToCheck.x, pixelToCheck.y);
				}
				for(int xOffset = -distance; xOffset <= distance; xOffset++) {
					Vector2Int pixelToCheck = new Vector2Int(pixel.x + xOffset, pixel.y - distance);
					if(hasPossibleColor(pixelToCheck))
						return texture.GetPixel(pixelToCheck.x, pixelToCheck.y);
				}
				for(int yOffset = 1-distance; yOffset <= -1+distance; yOffset++) {
					Vector2Int pixelToCheck = new Vector2Int(pixel.x - distance, pixel.y + yOffset);
					if(hasPossibleColor(pixelToCheck))
						return texture.GetPixel(pixelToCheck.x, pixelToCheck.y);
				}
			}
			return Color.white;

			bool hasPossibleColor(Vector2Int pixelToCheck) {
				return isPixelInTexture(pixelToCheck, textureSize) && !regionsToEliminate.Contains(pixelRegions[pixelToCheck]);
			}
		}

		bool isPixelColorEqual(Vector2Int pixel1, Vector2Int pixel2) {
			return texture.GetPixel(pixel1.x, pixel1.y) == texture.GetPixel(pixel2.x, pixel2.y);
		}
	}

	//normalized coordinates (not world coordinates)
	bool isPixelInTexture(Vector2Int pixel, Vector2Int texSize) {
		return (pixel.x >= 0 && pixel.x < texSize.x && pixel.y >= 0 && pixel.y < texSize.y);
	}

	bool isPixelInTerrain(Vector2Int pixel) {
		return (pixel.x >= 1 && pixel.x < textureSize.x-1 && pixel.y >= 1 && pixel.y < textureSize.y-1);
	}

	//areaSize: radius of area around a pixel to check for local minimum
	List<Vector2Int> findLocalMinima(Vector3[] meshVertices, int searchArea) {

		//check for every pixel if it is a local minimum for the chosen area around it
		List<Vector2Int> minimaList = new List<Vector2Int>();
		for(int meshY = 1; meshY < textureSize.y-2; meshY++) {
			for(int meshX = 1; meshX < textureSize.x-1; meshX++) {
				if(isLocalMinimum(meshX, meshY)) {
					minimaList.Add(new Vector2Int(meshX, meshY));
				}
			}
		}
		return minimaList;

		//check if there are vertices with lower high in the chosen area around
		bool isLocalMinimum(int x, int y) {
			bool isLocalMinimum = true;
			for(int offsetX = -searchArea; offsetX <= searchArea; offsetX++) {
				for(int offsetY = -searchArea; offsetY <= searchArea; offsetY++) {
					isLocalMinimum = isLocalMinimum && !isOtherPixelLower(x + offsetX, y + offsetY, x, y);
					if(!isLocalMinimum) {
						return false;
					}
				}
			}
			return isLocalMinimum;
		}

		//true if: pixel is in texture & pixelHeight is smaller than currentPixelHeight
		bool isOtherPixelLower(int x, int y, int currX, int currY) {
			return isPixelInTerrain(new Vector2Int(x, y)) && meshVertices[meshIndex(x, y)].y < meshVertices[meshIndex(currX, currY)].y;
		}

		int meshIndex(int x, int y) {
			return x + y * textureSize.x;
		}
	}

	//calc voronoi diagram: array of intensity values (0-2)
	private int[] calculateVoronoiDiagram(bool isTempDiagram) {
		Vector2Int[] centroids;
		if(useMinimaForCentroids) {
			centroids = new Vector2Int[localMinima.Count];;
			for(int i = 0; i < localMinima.Count; i++)
			{
				centroids[i] = localMinima[i];
			}
		} else {
			//roll centroids
			centroids = new Vector2Int[regionAmount];
			for(int i = 0; i < regionAmount; i++)
			{
				centroids[i] = new Vector2Int(Random.Range(0, textureSize.x), Random.Range(0, textureSize.y));
			}
		}
		
		//determine region id of each pixel
		int[] pixelRegions = new int[textureSize.x * textureSize.y];
        int index = 0;
		for(int y = 0; y < textureSize.y; y++)
		{
			for(int x = 0; x < textureSize.x; x++)
			{
				pixelRegions[index] = GetClosestCentroidIndex(new Vector2Int(x, y), centroids, isTempDiagram);
                index++;
			}
		}

		//determine intensity ids for regions
		int[] regionIntensityIDs;
		if (isTempDiagram) {
			regionIntensityIDs = CalcRegionIntensityIDs(isTempDiagram, tempRegionNeighbors);
		} else {
			regionIntensityIDs = CalcRegionIntensityIDs(isTempDiagram, precRegionNeighbors);
		}
		
		//determine intensity id for every pixel
		int[] pixelIntensityIDs = new int [textureSize.x * textureSize.y];
		for (int i = 0; i < pixelIntensityIDs.Length; i++) {
			int pixelIntensityID = regionIntensityIDs[pixelRegions[i]];
			pixelIntensityIDs[i] = pixelIntensityID;
		}
		return pixelIntensityIDs;

		int[] CalcRegionIntensityIDs(bool calcTempDiagram, List<int>[] regionNeighbors) {
			int[] regionIntensityIDs = new int[centroids.Length];
			for (int i = 0; i < centroids.Length; i++) {
				regionIntensityIDs[i] = -1;
			}
			for (int i = 0; i < centroids.Length; i++) {
				List<int> neighboringIntensityIDs = new List<int>();
				for (int j = 0; j < regionNeighbors[i].Count; j++) {
					int neighborID = regionNeighbors[i][j];
					if (!neighboringIntensityIDs.Contains(regionIntensityIDs[neighborID])) {
						neighboringIntensityIDs.Add(regionIntensityIDs[neighborID]);
					}
				}
				if (!neighboringIntensityIDs.Contains(0) && !neighboringIntensityIDs.Contains(2)) {
					regionIntensityIDs[i] = rollIntensityID(calcTempDiagram, 0, 3);
				} else if (!neighboringIntensityIDs.Contains(0) && neighboringIntensityIDs.Contains(2)) {
					regionIntensityIDs[i] = rollIntensityID(calcTempDiagram, 1, 3);
				} else if (neighboringIntensityIDs.Contains(0) && !neighboringIntensityIDs.Contains(2)) {
					regionIntensityIDs[i] = rollIntensityID(calcTempDiagram, 0, 2);
				} else {
					regionIntensityIDs[i] = 1;
				}
			}
			return regionIntensityIDs;
		}
	}

	private int rollIntensityID(bool calcTempDiagram, int lowestIntensityID, int highestIntensityID) {
		float weightFactor;
		if(calcTempDiagram) {
			weightFactor = averageTemp;
		} else {
			weightFactor = averagePrec;
		}
		int intensityID;
		float dieValue = Random.Range(0f, 1f);
		//there are 3 possible color IDs
		if((highestIntensityID - lowestIntensityID) == 3) {
			if(dieValue < 1-weightFactor) {
				intensityID = 0;
			} else {
				intensityID = 2;
			}
		} else {
			//there are 2 possible color IDs
			if(lowestIntensityID == 0) {
				//possible color IDs are 0 and 1
				if(dieValue < 1-weightFactor)
					intensityID = 0;
				else
					intensityID = 1;
			} else {
				//possible color IDs are 1 and 2
				if(dieValue < 1-weightFactor)
					intensityID = 1;
				else
					intensityID = 2;
			}
		}
		return intensityID;
	}
	int GetClosestCentroidIndex(Vector2Int pixelPos, Vector2Int[] centroids, bool calcTempDiagram) {
		float smallestDst = float.MaxValue;
		int nearestRegionID = 0;
		int secondNearestRegionID = 0;

		//find nearest region id
		for(int i = 0; i < centroids.Length; i++) {
			float distance = calcMinkowskiDistance(pixelPos, centroids[i], minkowskiLambda);
			if (distance < smallestDst) {
				nearestRegionID = i;
				smallestDst = distance;
			}
		}

		//find second nearest region id (determine neighboring regions)
		smallestDst = float.MaxValue;
		for(int i = 0; i < centroids.Length; i++)
		{
			if (i != nearestRegionID) {
				float distance = calcMinkowskiDistance(pixelPos, centroids[i], minkowskiLambda);
				if (distance < smallestDst) {
					secondNearestRegionID = i;
					smallestDst = distance;
				}
			}
		}

		//add neighboring region, if not already contained
		if (calcTempDiagram) {
			if (!tempRegionNeighbors[nearestRegionID].Contains(secondNearestRegionID)) {
				tempRegionNeighbors[nearestRegionID].Add(secondNearestRegionID);
			}
		} else {
			if (!precRegionNeighbors[nearestRegionID].Contains(secondNearestRegionID)) {
				precRegionNeighbors[nearestRegionID].Add(secondNearestRegionID);
			}
		}
		return nearestRegionID;
	}

	//manhatten: p=1, euclidean: p=2
	private float calcMinkowskiDistance(Vector2Int pointA, Vector2Int pointB, float lambda) {
		return Mathf.Pow(Mathf.Pow(Mathf.Abs(pointA.x-pointB.x),lambda) + Mathf.Pow(Mathf.Abs(pointA.y-pointB.y),lambda), 1/lambda);
	}

	//Textures have to be same size
	Texture2D MergeTextures(Texture2D firstTexture, Texture2D secondTexture) {
		Color[] pixelColors = new Color[textureSize.x * textureSize.y];

		int index = 0;
		for (int y = 0; y < textureSize.y; y++) {
			for (int x = 0; x < textureSize.x; x++) {
				Color firstColor = firstTexture.GetPixel(x,y);
				Color secondColor = secondTexture.GetPixel(x,y);
				pixelColors[index] = mergeColors(firstColor, secondColor, 0.5f);
				index++;
			}
		}

		Texture2D mergedTexture = GetTextureFromColorArray(pixelColors);
		return mergedTexture;
	}

	//pixelIntensities has size xSize*ySize of the texture
	//pixelIntensities has colors.size (3) distinct values
	//colors: map intensityIDs to color values
	private Texture2D GetTextureFromIntensityArray(int[] pixelIntensities, Dictionary<int, Color> colors)
	{
		Color[] pixelColors = new Color [pixelIntensities.Length];
		for (int i = 0; i < pixelIntensities.Length; i++) {
			pixelColors[i] = colors[pixelIntensities[i]];
		}

		if(showLocalMinima) {
			foreach(Vector2Int minimum in localMinima) {
				int index = minimum.x + minimum.y * textureSize.x;
				pixelColors[index] = Color.black;
			}
		}

		return GetTextureFromColorArray(pixelColors);
	}

	private Texture2D GetTextureFromColorArray(Color[] pixelColors) {
		Texture2D tex = new Texture2D(textureSize.x, textureSize.y);
		tex.filterMode = FilterMode.Point;
		tex.SetPixels(pixelColors);
		tex.Apply();
		return tex;
	}

	private static Color mergeColors(Color color1, Color color2, float mergeProportions) {
		return Color.Lerp(color1, color2, Mathf.Clamp(mergeProportions, 0f, 1f));
	}

	private Color addSaturation(Color oldColor, float satAddition) {
		Vector3 hsvColor = new Vector3(0,0,0);
		Color.RGBToHSV(oldColor, out hsvColor.x, out hsvColor.y, out hsvColor.z);
		return Color.HSVToRGB(hsvColor.x, Mathf.Min(hsvColor.y+satAddition, 1f), hsvColor.z);
	}

	private Texture2D addSaturationToTexture(Texture2D texture, float satAddition) {
		for(int y = 0; y < textureSize.y; y++) {
			for(int x = 0; x < textureSize.x; x++) {
				texture.SetPixel(x,y,addSaturation(texture.GetPixel(x,y),0.5f));
			}
		}
		texture.Apply();
		return texture;
	}

	private Sprite buildSprite(Texture2D texture) {
		return Sprite.Create(texture, new Rect(0, 0, textureSize.x, textureSize.y), Vector2.one * 0.5f);
	}

	public Texture2D getBiomeTexture() {
		return biomeTexture;
	}

	public bool isBiomeTextureGenerated() {
		return biomeGenerationFinished;
	}

	public Dictionary<int, Color> getTempColors() {
		return tempColors;
	}

	public Dictionary<int, Color> getPrecColors() {
		return precColors;
	}

	public Texture2D getSteepnessMap() {
		return steepnessMap;
	}

	public Texture2D[] getBiomeSplatmaps() {
		Texture2D[] resultArray = new Texture2D[3];
		resultArray[0] = biomeSplatmap_temp0;
		resultArray[1] = biomeSplatmap_temp1;
		resultArray[2] = biomeSplatmap_temp2;
		return resultArray;
	}
}