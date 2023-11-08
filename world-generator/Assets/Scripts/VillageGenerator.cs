using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class VillageGenerator : MonoBehaviour
{
    public TerrainGenerator terrainGenerator;
    public BiomeGenerator biomeGenerator;
    public bool updateVillageMap = false;


    [Header("Road Settings")]
    //factor used to scale resolution of main village map down
    public int resolutionDevider = 5;
    public int streetMaskResolution;
    public int streetPixelErosionCount;
    [Header("Village Area Settings")]
    //as vertex count
    public Vector2Int villageRadius_min_max;
    [Range(0f,1f)]
    //% of maximal possible size in vertices
    public float relativeMinVillageSize;
    public Vector3Int roadEntryCount_min_max_minDist;
    //between 0f and 1f
    public Vector2 possibleSteepness_min_max;
    //between 1 and inf
    public Vector2Int possibleWaterDistance_min_max;
    [Header("Building Settings")]
    public Vector2Int houseCount_min_max;
    //is building too close or far form neighbors?
    public Vector3Int sociability_tooClose_best_tooFar;
    //is building too close or far form religious building?
    public Vector3Int worship_tooClose_best_tooFar;
    //is building too close or far form roads?
    public Vector3Int accessibility_tooClose_best_tooFar;
    //is building too close or far form fortification buildings?
    public Vector3Int fortification_tooClose_best_tooFar;
    //if a building is near a point of interest, it is more interested in a higher spot (closer to POI)
    //if maxDistanceToPOI is exceeded, height has no more impact on the overall interest value
    public int geographicalDomination_maxDistanceToPOI;
    public bool generateRoadCycles;
    public int minRoadCycleLength;

    [Header("POI Settings")]
    //POI placement (reli/fort) only possible in the (x*100)% highest village pixels
    //if 1: no POI placement restriction; if 0.01: POI placement only on highest pixels
    [Range(0.01f,1f)]
    public float POIHeightRestriction;
    public Vector3Int fortificationCount_min_max_minDist;
    public Vector3Int religiousBuildingCount_min_max_minDist;
    
    private int roadCycleAngle = 70;
    private int[,] waterDistanceMap;
    private Vector2Int[] eightNeighborhoodCoords = {
        new Vector2Int(1,1),
        new Vector2Int(1,0),
        new Vector2Int(1,-1),
        new Vector2Int(0,-1),
        new Vector2Int(-1,-1),
        new Vector2Int(-1,0),
        new Vector2Int(-1,1),
        new Vector2Int(0,1)
    };

    private Vector2Int[] fourNeighborhoodCoords = {
        new Vector2Int(1,0),
        new Vector2Int(0,-1),
        new Vector2Int(-1,0),
        new Vector2Int(0,1)
    };

    public enum VillagePixelType {OutsideArea, StandardVillageArea, VillageBorder, VillageRoadEntry, MapRoadEntry, HouseSeed, Road, ReligiousBuildingSeed, FortificationSeed};

    private enum SeedingType {RoadSeeding, HouseSeeding, POISeeding};
    private VillagePixelType[,] villageMap;
    private VillagePixelType[,] lowResVillageMap;
    private bool villageMapGenerated;

    void Start() {
        villageMap = initMap(new Vector2Int(0,0), VillagePixelType.OutsideArea);
        villageMapGenerated = false;
        VillagePixelType[,] streetMask = initMap(new Vector2Int(1,1), VillagePixelType.OutsideArea);
        SaveTextureAsPNG(mapToStreetTexture(streetMask, new Vector2Int(1,1)), "Assets/Textures/streetMask.png");
    }

    void Update()
    {
        keepSliderConsistency();

        if(updateVillageMap) {
            updateVillageMap = false;

            //generate village
            Texture2D waterTex = generateWaterMap();
            SaveTextureAsPNG(waterTex, "Assets/Textures/waterDistanceMap.png");

            villageMap = generateVillageAreaMap();

            //generate country roads
            Texture2D lowResSteepnessTex = calcLowResSteepnessMap(biomeGenerator.getSteepnessMap(), resolutionDevider);
            SaveTextureAsPNG(lowResSteepnessTex, "Assets/Textures/lowResSteepnessTex.png");
            int[,] lowResWaterMap = calcLowResWaterMap(waterDistanceMap, resolutionDevider);

            lowResVillageMap = calcLowResVillageMap(villageMap, resolutionDevider);
            List<List<Vector2Int>> countryRoads = calculateCountryRoads(lowResSteepnessTex, lowResWaterMap);
            villageMap = projectLowResRoadsOnVillageMap(countryRoads, villageMap);

            Texture2D lowResVillageTexture = villageMapToVillageTexture(lowResVillageMap);
            Texture2D villageTexture = villageMapToVillageTexture(villageMap);
            SaveTextureAsPNG(lowResVillageTexture, "Assets/Textures/lowResVillageMap.png");
            SaveTextureAsPNG(villageTexture, "Assets/Textures/villageAreaMap.png");

            //update street map
            Texture2D streetTexture = villageMapToStreetTexture(streetMaskResolution);
            SaveTextureAsPNG(streetTexture, "Assets/Textures/streetMask.png");

            villageMapGenerated = true;
            AssetDatabase.Refresh();
        }
    }

    void keepSliderConsistency() {
        checkSlider(sociability_tooClose_best_tooFar);
        checkSlider(worship_tooClose_best_tooFar);
        checkSlider(accessibility_tooClose_best_tooFar);
        checkSlider(fortification_tooClose_best_tooFar);

        void checkSlider(Vector3Int interestFunction) {
            if(interestFunction.y < interestFunction.x) {
                interestFunction.y = interestFunction.x;
            }
            if(interestFunction.z < interestFunction.y) {
                interestFunction.z = interestFunction.y;
            }
        }
    }

    private Texture2D villageMapToVillageTexture(VillagePixelType[,] villageMap) {
        Texture2D resultTex = new Texture2D(villageMap.GetLength(0), villageMap.GetLength(1));
		resultTex.filterMode = FilterMode.Point;

        //build texture
        /*  Area colors:
                outside area        = black,
                village area        = white,
                village border      = pink,
                village road entry  = yellow,
                map road entry      = orange,
                building seed       = green,
                road                = gray,
                reli building seed  = blue,
                fortification seed  = red
        */
        for(int y = 0; y < villageMap.GetLength(1); y++) {
            for(int x = 0; x < villageMap.GetLength(0); x++) {
                switch(villageMap[x,y]) {
                    case VillagePixelType.StandardVillageArea:
                        resultTex.SetPixel(x,y, new Color(1, 1, 1, 1));
                        break;
                    case VillagePixelType.VillageBorder:
                        resultTex.SetPixel(x,y, new Color(1, 0, 1, 1));
                        break;
                    case VillagePixelType.VillageRoadEntry:
                        resultTex.SetPixel(x,y, new Color(1, 1, 0, 1));
                        break;
                    case VillagePixelType.MapRoadEntry:
                        resultTex.SetPixel(x,y, new Color(1, 0.55f, 0, 1));
                        break;
                    case VillagePixelType.HouseSeed:
                        resultTex.SetPixel(x,y, new Color(0, 1, 0, 1));
                        break;
                    case VillagePixelType.Road:
                        resultTex.SetPixel(x,y, new Color(0.75f, 0.75f, 0.75f, 1));
                        break;
                    case VillagePixelType.ReligiousBuildingSeed:
                        resultTex.SetPixel(x,y, new Color(0, 0, 1, 1));
                        break;
                    case VillagePixelType.FortificationSeed:
                        resultTex.SetPixel(x,y, new Color(1, 0, 0, 1));
                        break;
                    case VillagePixelType.OutsideArea:
                        resultTex.SetPixel(x,y, new Color(0, 0, 0, 1));
                        break;
                }
            }
        }
        return resultTex;
    }

    private Texture2D villageMapToStreetTexture(int resolutionFactor) {
        Vector2Int meshSize = terrainGenerator.getUncondensedMeshSize();
        Vector2Int newResolution = new Vector2Int(meshSize.x*resolutionFactor,meshSize.y*resolutionFactor);

        VillagePixelType[,] streetMask = initMap(newResolution, VillagePixelType.OutsideArea);
		
        //build texture
        /*  Area colors:
                road entry          = black,
                road                = black,
                else                = white
        */

        transformMapToNewResolution();

        straightenStreets();

        for(int i = 0; i < streetPixelErosionCount; i++) {
            streetMask = erodeMap(VillagePixelType.Road, streetMask);
        }

        Texture2D resultTex = new Texture2D(newResolution.x, newResolution.y);
        resultTex.filterMode = FilterMode.Point;

        for(int y = 0; y < newResolution.y; y++) {
            for(int x = 0; x < newResolution.x; x++) {
                switch(streetMask[x,y]) {
                    case VillagePixelType.VillageRoadEntry:
                        resultTex.SetPixel(x,y, Color.black);
                        break;
                    case VillagePixelType.Road:
                        resultTex.SetPixel(x,y, Color.black);
                        break;
                    default:
                        resultTex.SetPixel(x,y, Color.white);
                        break;
                }
            }
        }

        return mapToStreetTexture(streetMask, newResolution);

        VillagePixelType[,] erodeMap(VillagePixelType erosionPixelType, VillagePixelType[,] map) {
            VillagePixelType[,] resultMap = copyMap(map);
            
            for(int y = 0; y < map.GetLength(0); y++) {
                for(int x = 0; x < map.GetLength(1); x++) {
                    if(map[x,y] == erosionPixelType) {
                        if(x-1 >= 0 && map[x-1,y] != erosionPixelType) {
                            resultMap[x,y] = map[x-1,y];
                        } else if(x+1 < map.GetLength(1) && map[x+1,y] != erosionPixelType) {
                            resultMap[x,y] = map[x+1,y];
                        } else if(y-1 >= 0 && map[x,y-1] != erosionPixelType) {
                            resultMap[x,y] = map[x,y-1];
                        } else if(y+1 < map.GetLength(0) && map[x,y+1] != erosionPixelType) {
                            resultMap[x,y] = map[x,y+1];
                        }
                    }
                }
            }
            return resultMap;
        }

        void straightenStreets() {
            for(int i = 0; i < resolutionFactor-1; i++) {
                VillagePixelType[,] lookupStreetMask = copyMap(streetMask);
                for(int y = 0; y < streetMask.GetLength(1); y++) {
                    for(int x = 0; x < streetMask.GetLength(0); x++) {
                        setPixelIfCornered(x,y, lookupStreetMask);
                    }
                }
            }
        }

        void setPixelIfCornered(int x, int y, VillagePixelType[,] lookupStreetMask) {
            if( lookupStreetMask[x, y] != VillagePixelType.VillageRoadEntry ||
                lookupStreetMask[x, y] != VillagePixelType.Road
            ) {
                int neighborStreetPixelCount = 0;
                foreach(Vector2Int offset in fourNeighborhoodCoords) {
                    Vector2Int neighborPixel = new Vector2Int(x + offset.x, y + offset.y);
                    if(isPixelInTexture(neighborPixel, newResolution, 0)) {
                        if (lookupStreetMask[neighborPixel.x, neighborPixel.y] == VillagePixelType.VillageRoadEntry ||
                            lookupStreetMask[neighborPixel.x, neighborPixel.y] == VillagePixelType.Road
                        ) {
                            neighborStreetPixelCount++;
                            if(neighborStreetPixelCount >= 2) {
                                streetMask[x, y] = VillagePixelType.Road;
                                return;
                            }
                        }
                    }
                }
            }
        }

        void transformMapToNewResolution() {
            for(int y = 0; y < meshSize.y; y++) {
                for(int x = 0; x < meshSize.x; x++) {
                    transformPixelToNewResolution(x * resolutionFactor, y * resolutionFactor, villageMap[x,y]);
                }
            }

            void transformPixelToNewResolution(int xCoord, int yCoord, VillagePixelType pixelType) {
                for(int y = 0; y < resolutionFactor; y++) {
                    for(int x = 0; x < resolutionFactor; x++) {
                        switch(pixelType) {
                            case VillagePixelType.VillageRoadEntry:
                                streetMask[xCoord + x, yCoord + y] = VillagePixelType.Road;
                                break;
                            case VillagePixelType.Road:
                                streetMask[xCoord + x, yCoord + y] = VillagePixelType.Road;
                                break;
                            default:
                                streetMask[xCoord + x, yCoord + y] = VillagePixelType.OutsideArea;
                                break;
                        }
                    }
                }
            }
        }
    }

    Texture2D mapToStreetTexture(VillagePixelType[,] streetMask, Vector2Int resolution) {
        Texture2D resultTex = new Texture2D(resolution.x, resolution.y);
        resultTex.filterMode = FilterMode.Point;

        for(int y = 0; y < resolution.y; y++) {
            for(int x = 0; x < resolution.x; x++) {
                switch(streetMask[x,y]) {
                    case VillagePixelType.VillageRoadEntry:
                        resultTex.SetPixel(x,y, Color.black);
                        break;
                    case VillagePixelType.Road:
                        resultTex.SetPixel(x,y, Color.black);
                        break;
                    default:
                        resultTex.SetPixel(x,y, Color.white);
                        break;
                }
            }
        }

        return resultTex;
    }

    VillagePixelType[,] calcLowResVillageMap(VillagePixelType[,] villageMap, int resolutionDevider) {
        int resultXSize = Mathf.FloorToInt(villageMap.GetLength(0)/resolutionDevider);
        int resultYSize = Mathf.FloorToInt(villageMap.GetLength(1)/resolutionDevider);
        VillagePixelType[,] resultMap = initMap(new Vector2Int(resultXSize,resultYSize), VillagePixelType.OutsideArea);

        for(int y = 0; y < resultYSize; y++) {
            for(int x = 0; x < resultXSize; x++) {
                resultMap[x,y] = avgPixelType(x,y);
            }
        }

        return resultMap;

        //avg values in area (resolutionDevider * resolutionDevider) to one pixel
        VillagePixelType avgPixelType(int startX, int startY) {
            int outsidePixelCount = 0;
            int villagePixelCount = 0;
            int xLoopEnd = (startX+1)*resolutionDevider;
            int yLoopEnd = (startY+1)*resolutionDevider;
            if(villageMap.GetLength(0) - xLoopEnd < resolutionDevider) {
                xLoopEnd = villageMap.GetLength(0);
            }
            if(villageMap.GetLength(1) - yLoopEnd < resolutionDevider) {
                yLoopEnd = villageMap.GetLength(1);
            }

            for(int y = startY*resolutionDevider; y < yLoopEnd; y++) {
                for(int x = startX*resolutionDevider; x < xLoopEnd; x++) {
                    if(villageMap[x,y] == VillagePixelType.VillageRoadEntry) {
                        return VillagePixelType.VillageRoadEntry;
                    } else if(villageMap[x,y] == VillagePixelType.MapRoadEntry) {
                        return VillagePixelType.MapRoadEntry;
                    } else if(villageMap[x,y] == VillagePixelType.OutsideArea) {
                        outsidePixelCount++;
                    } else {
                        villagePixelCount++;
                    }
                }
            }
            if(outsidePixelCount > villagePixelCount) {
                return VillagePixelType.OutsideArea;
            } else {
                return VillagePixelType.StandardVillageArea;
            }
        }
    }

    int[,] calcLowResWaterMap(int[,] waterDistanceMap, int resolutionDevider) {
        int resultXSize = Mathf.FloorToInt(waterDistanceMap.GetLength(0)/resolutionDevider);
        int resultYSize = Mathf.FloorToInt(waterDistanceMap.GetLength(1)/resolutionDevider);
        int[,] resultMap = initMap(new Vector2Int(resultXSize, resultYSize), -1);

        for(int y = 0; y < resultYSize; y++) {
            for(int x = 0; x < resultXSize; x++) {
                Vector2Int highResCoords = new Vector2Int(x*resolutionDevider, y*resolutionDevider);
                if(waterDistanceMap[highResCoords.x,highResCoords.y] == -1) {
                    resultMap[x,y] = -1;
                } else {
                    if(waterDistanceMap[highResCoords.x,highResCoords.y] < resolutionDevider) {
                        resultMap[x,y] = 0;
                    } else {
                        resultMap[x,y] = waterDistanceMap[highResCoords.x,highResCoords.y];
                    }
                }
            }
        }
        return resultMap;
    }

    Texture2D calcLowResSteepnessMap(Texture2D steepnessMap, int resolutionDevider) {
        int resultXSize = Mathf.FloorToInt(steepnessMap.width/resolutionDevider);
        int resultYSize = Mathf.FloorToInt(steepnessMap.height/resolutionDevider);
        Texture2D resultTex = new Texture2D(resultXSize, resultYSize);
		resultTex.filterMode = FilterMode.Point;

        for(int y = 0; y < resultYSize; y++) {
            for(int x = 0; x < resultXSize; x++) {
                resultTex.SetPixel(x, y, new Color(avgSteepnessArea(x,y,steepnessMap),0,0));
            }
        }

        return resultTex;
    }

    //avg values in area (resolutionDevider * resolutionDevider) to one pixel
    //input coordinates in low res; steepnessMap in high res
    float avgSteepnessArea(int startX, int startY, Texture2D steepnessMap) {
        float steepnessSum = 0;
        int xLoopEnd = (startX+1)*resolutionDevider;
        int yLoopEnd = (startY+1)*resolutionDevider;
        if(steepnessMap.width - xLoopEnd < resolutionDevider) {
            xLoopEnd = steepnessMap.width;
        }
        if(steepnessMap.height - yLoopEnd < resolutionDevider) {
            yLoopEnd = steepnessMap.height;
        }
        
        int pixelCount = 0;
        for(int y = startY*resolutionDevider; y < yLoopEnd; y++) {
            for(int x = startX*resolutionDevider; x < xLoopEnd; x++) {
                if(isPixelInTexture(new Vector2Int(x,y), new Vector2Int(steepnessMap.width, steepnessMap.height), 0)) {
                    steepnessSum += steepnessMap.GetPixel(x, y).r;
                    pixelCount++;
                }
            }
        }
        return steepnessSum/pixelCount;
    }

    //transform low resolution village map roads onto normal village map
    private VillagePixelType[,] projectLowResRoadsOnVillageMap(List<List<Vector2Int>> countryRoads, VillagePixelType[,] villageMap) {
        //VillagePixelType[,] tmpMap = initMap(new Vector2Int(villageMap.GetLength(0),villageMap.GetLength(1)), VillagePixelType.OutsideArea);
        List<Vector2Int> roadWayPoints = new List<Vector2Int>();
        List<Vector2Int> villageEntryPoints = new List<Vector2Int>();

        foreach(List<Vector2Int> countryRoad in countryRoads) {
            if(countryRoad.Count > 0) {
                VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.VillageRoadEntry};
                Vector2Int closestVillageEntry = getClosestPixelOfType(normalCoodsFromLowRes(countryRoad[0]), soughtPixelTypes, villageMap);
                connect2Points(closestVillageEntry, normalCoodsFromLowRes(countryRoad[0]));

                for(int i = 0; i < countryRoad.Count-1; i++) {
                    Vector2Int startCoords = normalCoodsFromLowRes(countryRoad[i]);
                    Vector2Int endCoords = normalCoodsFromLowRes(countryRoad[i+1]);
                    connect2Points(startCoords, endCoords);
                }

                soughtPixelTypes = new VillagePixelType[] {VillagePixelType.MapRoadEntry};
                Vector2Int closestMapEntry = getClosestPixelOfType(normalCoodsFromLowRes(countryRoad[countryRoad.Count-1]), soughtPixelTypes, villageMap);
                connect2Points(closestMapEntry, normalCoodsFromLowRes(countryRoad[countryRoad.Count-1]));
            }
        }
        return villageMap;

        Vector2Int normalCoodsFromLowRes(Vector2Int lowResCoords) {
            int xCoord = Mathf.CeilToInt((lowResCoords.x*resolutionDevider) + resolutionDevider/2);
            int yCoord = Mathf.CeilToInt((lowResCoords.y*resolutionDevider) + resolutionDevider/2);
            return new Vector2Int(xCoord, yCoord);
        }

        void connect2Points(Vector2Int start, Vector2Int end) {
            Vector2Int currentPixel = start;
            while(currentPixel != end) {
                float closestDistToEnd = float.MaxValue;
                Vector2Int closestNeighborToEnd = new Vector2Int(-100, -100);
                foreach(Vector2Int neighborOffset in eightNeighborhoodCoords) {
                    Vector2Int neighbor = new Vector2Int(currentPixel.x+neighborOffset.x, currentPixel.y+neighborOffset.y);
                    float distToEnd = Vector2Int.Distance(neighbor, end);
                    if( isPixelInTexture(neighbor, new Vector2Int(villageMap.GetLength(0), villageMap.GetLength(1)), 0) &&
                        distToEnd < closestDistToEnd
                    ) {
                        closestNeighborToEnd = neighbor;
                        closestDistToEnd = distToEnd;
                    }
                }
                villageMap[closestNeighborToEnd.x,closestNeighborToEnd.y] = VillagePixelType.Road;
                currentPixel = closestNeighborToEnd;
            }
        }
    }

    private List<List<Vector2Int>> calculateCountryRoads(Texture2D lowResSteepnessMap, int[,] lowResWaterMap) {
        //determine village entries
        List<Vector2Int> villageEntries = new List<Vector2Int>();
        List<Vector2Int> mapEntries = new List<Vector2Int>();
        for(int y = 0; y < lowResVillageMap.GetLength(1); y++) {
            for(int x = 0; x < lowResVillageMap.GetLength(0); x++) {
                if(lowResVillageMap[x,y] == VillagePixelType.VillageRoadEntry) {
                    villageEntries.Add(new Vector2Int(x,y));
                }
                if(lowResVillageMap[x,y] == VillagePixelType.MapRoadEntry) {
                    mapEntries.Add(new Vector2Int(x,y));
                }
            }
        }
        
        //connect map entries to village entries
        List<List<Vector2Int>> newRoads = new List<List<Vector2Int>>();
        for(int i = 0; i < villageEntries.Count; i++) {
            VillagePixelType[] designatedAreaTypes = {VillagePixelType.OutsideArea, VillagePixelType.Road};
            List<Vector2Int> newRoad = calcShortestRoad(villageEntries[i], mapEntries[i], lowResVillageMap, lowResSteepnessMap, lowResWaterMap, 0f, designatedAreaTypes);
            newRoads.Add(newRoad);
        }

        foreach(List<Vector2Int> road in newRoads) {
            foreach(Vector2Int roadPixel in road) {
                lowResVillageMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
            }
        }
        return newRoads;
    }

    private VillagePixelType[,] generateVillageAreaMap() {
        Vector3[] terrainVertices = terrainGenerator.getUncondensedTerrainVertices();
        Vector2Int meshSize = terrainGenerator.getUncondensedMeshSize();
        Texture2D steepnessMap = biomeGenerator.getSteepnessMap();

        VillagePixelType[,] resultVillageAreaMap = initMap(meshSize, VillagePixelType.OutsideArea);
        bool waterExists = (waterDistanceMap[0,0] != -1);
        int maxIterations = 500;
        int villageVertexCount = 0;
        int villageRadius = Random.Range(villageRadius_min_max.x, villageRadius_min_max.y+1);

        //calculate minimal village size in vertices
        int villageDiameter = villageRadius*2+1;
        int halfVillageArea = 0;
        for(int i = villageDiameter-2; i > 0; i = i-2) {
            halfVillageArea += i;
        }
        int maxPossibleVillageSize = 2*halfVillageArea + villageDiameter;
        int absoluteMinVillageSize = (int) (maxPossibleVillageSize * relativeMinVillageSize);

        int villageBorderPixelCount = 0;

        //find a village area that is big enough to satisfy absoluteMinVillageSize-parameter
        int iteration = 0;
        while(villageVertexCount < absoluteMinVillageSize && iteration < maxIterations) {
            resultVillageAreaMap = initMap(meshSize, VillagePixelType.OutsideArea);
            if(!calcSeedCoords()) {
                return resultVillageAreaMap;
            }

            calcVillageArea();

            iteration++;
            if(iteration >= maxIterations) {
                Debug.Log("No big enough village area was found in " + maxIterations + " iterations.");
                return resultVillageAreaMap;
            }
        }

        bool firstRoadCalculated = false;
        //calculate road entry points
        int entryPointCount = Random.Range(roadEntryCount_min_max_minDist.x, roadEntryCount_min_max_minDist.y+1);
        resultVillageAreaMap = distributePointsIntoAreaEvenly(SeedingType.RoadSeeding, resultVillageAreaMap, entryPointCount, roadEntryCount_min_max_minDist.z, VillagePixelType.VillageBorder, VillagePixelType.VillageRoadEntry);

        //calculate map entry points
        //setup map edge list
        List<int> mapEdgeIDs = new List<int>();
        for(int i = 0; i < entryPointCount; i++) {
            mapEdgeIDs.Add(i % 4);
        }
        List<int> randomMapEdgeIDs = randomlyOrderNumbers(mapEdgeIDs);

        //determine map entries on map edge
        List<Vector2Int> mapEntries = new List<Vector2Int>();
        Vector2Int mapSize = terrainGenerator.getUncondensedMeshSize();
        for(int i = 0; i < entryPointCount; i++) {
            for(int tries = 0; tries < maxIterations; tries++) {
                Vector2Int possibleCoords = getRandomCoordsOnEdge(mapSize, randomMapEdgeIDs[i]);
                float avgSteepnessAroundCoords = avgSteepnessArea(
                    Mathf.FloorToInt(possibleCoords.x/resolutionDevider),
                    Mathf.FloorToInt(possibleCoords.y/resolutionDevider),
                    steepnessMap
                );

                if( avgSteepnessAroundCoords < possibleSteepness_min_max.y) {
                    mapEntries.Add(possibleCoords);
                    break;
                }
                if(tries == maxIterations-1) {
                    Debug.Log("Found no fitting location for map entry point " + (entryPointCount+1) + " on map edge " + randomMapEdgeIDs[i] + ".");
                }
            }
        }
        foreach(Vector2Int entry in mapEntries) {
            resultVillageAreaMap[entry.x, entry.y] = VillagePixelType.MapRoadEntry;
        }

        //calculate religius building seeds
        int reliBuildingCount = Random.Range(religiousBuildingCount_min_max_minDist.x, religiousBuildingCount_min_max_minDist.y+1);
        resultVillageAreaMap = distributePointsIntoAreaEvenly(SeedingType.POISeeding, resultVillageAreaMap, reliBuildingCount, religiousBuildingCount_min_max_minDist.z, VillagePixelType.StandardVillageArea, VillagePixelType.ReligiousBuildingSeed);

        //calculate fortification seeds
        int fortificationCount = Random.Range(fortificationCount_min_max_minDist.x, fortificationCount_min_max_minDist.y+1);
        resultVillageAreaMap = distributePointsIntoAreaEvenly(SeedingType.POISeeding, resultVillageAreaMap, fortificationCount, fortificationCount_min_max_minDist.z, VillagePixelType.StandardVillageArea, VillagePixelType.FortificationSeed);

        //calc roads from unconnected road entries
        VillagePixelType[] designatedAreaTypes = new VillagePixelType[] {VillagePixelType.VillageRoadEntry};
        List<Vector2Int> unconnectedRoadEntries = getDesignatedPixels(resultVillageAreaMap, designatedAreaTypes);
        foreach(Vector2Int roadEntry in unconnectedRoadEntries) {
            if(!hasPixelSpecificNeighbor(roadEntry, VillagePixelType.Road, resultVillageAreaMap)) {
                VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.Road};
                Vector2Int connectionPoint = getClosestPixelOfType(roadEntry, soughtPixelTypes, resultVillageAreaMap);

                designatedAreaTypes = new VillagePixelType[] {VillagePixelType.StandardVillageArea, VillagePixelType.Road};
                List<Vector2Int> newRoad = calcShortestRoad(roadEntry, connectionPoint, resultVillageAreaMap, biomeGenerator.getSteepnessMap(), waterDistanceMap, 0f, designatedAreaTypes);
                foreach(Vector2Int roadPixel in newRoad) {
                    resultVillageAreaMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
                }
            }
        }

        //calculate house seeds
        int buildingCount = Random.Range(houseCount_min_max.x, houseCount_min_max.y+1);
        resultVillageAreaMap = distributePointsIntoAreaEvenly(SeedingType.HouseSeeding, resultVillageAreaMap, buildingCount, 0, VillagePixelType.StandardVillageArea, VillagePixelType.HouseSeed);

        /*
        //maybe later use; right now it makes chess patterns into neighboring roads
        if(generateRoadCycles) {
            thinOutRoads(resultVillageAreaMap);
        }
        */

        return resultVillageAreaMap;

        Vector2Int getRandomCoordsOnEdge(Vector2Int mapSize, int mapEdgeID) {
            Vector2Int resultCoords = new Vector2Int(0,0);

            //fully randomize the other coord for on the picked edge
            switch(mapEdgeID) {
                case 0:
                    resultCoords = new Vector2Int(0,Random.Range(0, mapSize.y));
                    break;
                case 1:
                    resultCoords = new Vector2Int(mapSize.x-1,Random.Range(0, mapSize.y));
                    break;
                case 2:
                    resultCoords = new Vector2Int(Random.Range(0, mapSize.x), 0);
                    break;
                case 3:
                    resultCoords = new Vector2Int(Random.Range(0, mapSize.x), mapSize.y-1);
                    break;
            }
            
            return resultCoords;
        }

        //remove non-connecting road pixel (means: when all house & road neighbors are connected with each other)
        void thinOutRoads(VillagePixelType[,] villageMap) {
            for(int y = 0; y < meshSize.y; y++) {
                for(int x = 0; x < meshSize.x; x++) {
                    if(villageMap[x,y] == VillagePixelType.Road) {

                        List<Vector2Int> specialNeighborPixels_pixelSet = getSpecialNeighbors(new Vector2Int(x,y), eightNeighborhoodCoords);

                        if(specialNeighborPixels_pixelSet.Count <= 1) {
                            villageMap[x,y] = VillagePixelType.StandardVillageArea;
                        } else {
                            bool deleteRoadPixel = true;
                            foreach(Vector2Int neighbor in specialNeighborPixels_pixelSet) {
                                List<Vector2Int> specialNeighborPixels_neighborSet = getSpecialNeighbors(new Vector2Int(neighbor.x,neighbor.y), eightNeighborhoodCoords);
                                List<Vector2Int> neighborIntersection = specialNeighborPixels_pixelSet.Intersect(specialNeighborPixels_neighborSet).ToList();
                                
                                //if there exists specialNeighbor of pixel, that has no common specialNeighbor with pixel -> pixel is essential for connection (dont delete)
                                if(neighborIntersection.Count == 0) {
                                    deleteRoadPixel = false;
                                    break;
                                }
                            }

                            if(deleteRoadPixel) {
                                villageMap[x,y] = VillagePixelType.StandardVillageArea;
                            }
                        }
                    }
                }
            }

            List<Vector2Int> getSpecialNeighbors(Vector2Int pixel, Vector2Int[] neighborhoodCoords) {
                List<Vector2Int> specialNeighborPixels = new List<Vector2Int>();
                foreach(Vector2Int coords in neighborhoodCoords) {
                    Vector2Int neighbor = new Vector2Int(pixel.x+coords.x, pixel.y+coords.y);

                    if( isPixelInTexture(neighbor, meshSize, 0) &&
                        (villageMap[neighbor.x, neighbor.y] == VillagePixelType.Road ||
                        villageMap[neighbor.x, neighbor.y] == VillagePixelType.HouseSeed ||
                        villageMap[neighbor.x, neighbor.y] == VillagePixelType.ReligiousBuildingSeed ||
                        villageMap[neighbor.x, neighbor.y] == VillagePixelType.FortificationSeed ||
                        villageMap[neighbor.x, neighbor.y] == VillagePixelType.VillageRoadEntry)
                    ) {
                        specialNeighborPixels.Add(neighbor);
                    }
                }
                return specialNeighborPixels;
            }
        }

        bool hasPixelSpecificNeighbor(Vector2Int pixel, VillagePixelType soughtNeighborType, VillagePixelType[,] villageMap) {
            bool result = false;
            foreach(Vector2Int neighborOffset in eightNeighborhoodCoords) {
                Vector2Int neighbor = new Vector2Int(pixel.x+neighborOffset.x, pixel.y+neighborOffset.y);
                if( isPixelInTexture(neighbor, meshSize, 0) &&
                    villageMap[neighbor.x, neighbor.y] == soughtNeighborType
                ) {
                    result = true;
                }
            }
            return result;
        }

        //random even distribution of another VillagePixelType on some designated VillagePixelType-Area
        VillagePixelType[,] distributePointsIntoAreaEvenly(SeedingType seedingType, VillagePixelType[,] initMap, int newPixelCount, float minNewPixelDistance, VillagePixelType designatedAreaType, VillagePixelType newPixelType) {
            VillagePixelType[,] villageMap = copyMap(initMap);

            VillagePixelType[] designatedAreaTypes = new VillagePixelType[] {designatedAreaType};
            List<Vector2Int> designatedAreaPixels = getDesignatedPixels(villageMap, designatedAreaTypes);
            List<Vector2Int> newPoints = new List<Vector2Int>();

            //determine which designatedAreaType-pixels should become newPixelType-pixels
            int iteration = 0;
            while(newPoints.Count < newPixelCount) {
                int newPointID = Random.Range(0, designatedAreaPixels.Count);
                Vector2Int newPointCoords = designatedAreaPixels[newPointID];
                bool isPointApproved = false;

                switch(seedingType) {
                    case SeedingType.RoadSeeding:
                        float distanceToNearestRoadSeed = calcLowestPixelDistance(newPointCoords, newPoints, minNewPixelDistance);
                        isPointApproved = distanceToNearestRoadSeed >= (float) minNewPixelDistance;
                        break;
                    case SeedingType.HouseSeeding:
                        float newPointProbability = calcInterestValue(newPointCoords, villageMap);
                        isPointApproved = newPointProbability > Random.Range(0f, 1f);
                        break;
                    case SeedingType.POISeeding:
                        float distanceToNearestPOI = calcLowestPixelDistance(newPointCoords, newPoints, minNewPixelDistance);
                        Dictionary<Vector2Int, float> highestAreaPoints = getXHighestPointsInArea(designatedAreaPixels, Mathf.CeilToInt(designatedAreaPixels.Count * POIHeightRestriction));
                        isPointApproved = highestAreaPoints.ContainsKey(newPointCoords) && distanceToNearestPOI >= (float) minNewPixelDistance;
                        break;
                }

                if( !newPoints.Contains(newPointCoords) && isPointApproved) {
                    newPoints.Add(newPointCoords);
                    //change determined designatedAreaType-pixels into newPixelType-pixels
                    villageMap[newPointCoords.x, newPointCoords.y] = newPixelType;

                    //calc roads to religious building/fortification seeds
                    if(seedingType == SeedingType.POISeeding) {
                        Vector2Int connectionPoint;
                        if(!firstRoadCalculated) {
                            VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.VillageRoadEntry};
                            connectionPoint = getFarthestPixelOfType(newPointCoords, soughtPixelTypes, villageMap);
                            firstRoadCalculated= true;
                        } else {
                            VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.VillageRoadEntry, VillagePixelType.Road};
                            connectionPoint = getClosestPixelOfType(newPointCoords, soughtPixelTypes, villageMap);
                            if(villageMap[connectionPoint.x, connectionPoint.y] == VillagePixelType.VillageRoadEntry) {
                                VillagePixelType[] soughtPixelTypes_alt = new VillagePixelType[] {VillagePixelType.Road};
                                Vector2Int connectionPoint_alt = getClosestPixelOfType(newPointCoords, soughtPixelTypes_alt, villageMap);

                                designatedAreaTypes = new VillagePixelType[] {VillagePixelType.StandardVillageArea, VillagePixelType.Road};
                                List<Vector2Int> roadToNewPoint_alt = calcShortestRoad(connectionPoint_alt, newPointCoords, villageMap,biomeGenerator.getSteepnessMap(), waterDistanceMap, 0f, designatedAreaTypes);
                                foreach(Vector2Int roadPixel in roadToNewPoint_alt) {
                                    villageMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
                                }
                            }
                        }
                        
                        designatedAreaTypes = new VillagePixelType[] {VillagePixelType.StandardVillageArea, VillagePixelType.Road};
                        List<Vector2Int> roadToNewPoint = calcShortestRoad(connectionPoint, newPointCoords, villageMap, biomeGenerator.getSteepnessMap(), waterDistanceMap, 0f, designatedAreaTypes);
                        foreach(Vector2Int roadPixel in roadToNewPoint) {
                            villageMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
                        }
                    }

                    //calc roads to standard building seeds
                    if(seedingType == SeedingType.HouseSeeding) {
                        VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.VillageRoadEntry};
                        Vector2Int closestRoadEntry = getClosestPixelOfType(newPointCoords, soughtPixelTypes, villageMap);

                        designatedAreaTypes = new VillagePixelType[] {VillagePixelType.StandardVillageArea, VillagePixelType.Road};
                        List<Vector2Int> roadToBuilding = calcShortestRoad(closestRoadEntry, newPointCoords, villageMap, biomeGenerator.getSteepnessMap(), waterDistanceMap, 0f, designatedAreaTypes);
                        foreach(Vector2Int roadPixel in roadToBuilding) {
                            villageMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
                        }
                    }
                }

                iteration++;
                if(iteration >= maxIterations) {
                    Debug.Log("Could not find fitting " + designatedAreaType.ToString() + " pixels for all " + newPixelCount + " necessary " + newPixelType.ToString() + " pixels in " + maxIterations + " iterations.");
                    break;
                }
            }
            
            if(generateRoadCycles) {
                //calculate road cycles after all connecting roads are set
                designatedAreaTypes = new VillagePixelType[] {VillagePixelType.HouseSeed};
                designatedAreaPixels = getDesignatedPixels(villageMap, designatedAreaTypes);
                foreach(Vector2Int houseSeed in designatedAreaPixels) {
                    List<Vector2Int> roadCycle = calcRoadCycle(houseSeed, villageMap);

                    //only set roadCycles that are longer than best sociability distance
                    if(roadCycle.Count >= minRoadCycleLength) {

                        //only set roadCycle if its not neighboring too much of another road
                        if(unrelatedRoadNeighborCountOfPath(roadCycle) <= 2) {

                            //if roadCycle is empty, no cycle was found and no roads are set
                            if(roadCycle.Count > 0) {
                                foreach(Vector2Int roadPixel in roadCycle) {
                                    villageMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
                                }
                            }
                        }
                    }
                }
            }

            return villageMap;

            //number of neighboring road pixels that are not on the designated path
            int unrelatedRoadNeighborCountOfPath(List<Vector2Int> path) {
                int neighborCount = 0;
                foreach(Vector2Int pixel in path) {
                    foreach(Vector2Int neighborOffset in eightNeighborhoodCoords) {
                        Vector2Int neighbor = new Vector2Int(pixel.x+neighborOffset.x, pixel.y+neighborOffset.y);
                        if(isPixelInTexture(neighbor, meshSize, 0)) {
                            if( villageMap[neighbor.x, neighbor.y] == VillagePixelType.Road &&
                                !path.Contains(neighbor)
                            ) {
                                neighborCount++;
                            }
                        }
                    }
                }
                return neighborCount;
            }

            List<Vector2Int> calcRoadCycle(Vector2Int houseSeed, VillagePixelType[,] villageMap) {
                //find all road neighbors of building seed
                List<Vector2Int> roadNeighbors = new List<Vector2Int>();
                foreach(Vector2Int neighborCoords in eightNeighborhoodCoords) {
                    Vector2Int neighbor = new Vector2Int(houseSeed.x+neighborCoords.x, houseSeed.y+neighborCoords.y);
                    if( isPixelInTexture(neighbor, meshSize, 0) &&
                        villageMap[neighbor.x, neighbor.y] == VillagePixelType.Road
                    ) {
                        roadNeighbors.Add(neighbor);
                    }
                }

                List<Vector2Int> roadCycle = new List<Vector2Int>();

                //if there is a clear direction from where the road is coming to the building: calc cycle
                if(roadNeighbors.Count == 1) {
                    Vector2Int neighborRoadPixel = roadNeighbors.Last();
                    Vector2Int direction = houseSeed - neighborRoadPixel;
                    VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.Road};
                    Vector2Int closestRoadInDirection = getClosestPixelOfTypeInDirection(houseSeed, direction, soughtPixelTypes, villageMap);
                    //if a closest road pixel was found
                    if(closestRoadInDirection.x >= 0) {
                        float roadWeight = Mathf.Lerp(possibleSteepness_min_max.x, possibleSteepness_min_max.y, 0.4f);

                        designatedAreaTypes = new VillagePixelType[] {VillagePixelType.StandardVillageArea, VillagePixelType.Road};
                        roadCycle = calcShortestRoad(neighborRoadPixel, closestRoadInDirection, villageMap, biomeGenerator.getSteepnessMap(), waterDistanceMap, roadWeight, designatedAreaTypes);
                        foreach(Vector2Int roadPixel in roadCycle) {
                            villageMap[roadPixel.x, roadPixel.y] = VillagePixelType.Road;
                        }
                    }
                }
                return roadCycle;
            }

            //only for building seeds
            //a value in the interval of [0,1] that influences the likelyhood of a successful building seed placement
            float calcInterestValue(Vector2Int newPointCoords, VillagePixelType[,] villageMap) {
                List<float> interestValues = new List<float>();

                //sociability
                VillagePixelType[] designatedAreaTypes = new VillagePixelType[] {VillagePixelType.HouseSeed};
                List<Vector2Int> housePixels = getDesignatedPixels(villageMap, designatedAreaTypes);
                float sociabilityInterestValue =
                    calcInterestFeature(    sociability_tooClose_best_tooFar.x,
                                            sociability_tooClose_best_tooFar.y,
                                            sociability_tooClose_best_tooFar.z,
                                            newPointCoords,
                                            housePixels
                                        );
                interestValues.Add(sociabilityInterestValue);

                //accessibility
                designatedAreaTypes = new VillagePixelType[] {VillagePixelType.Road};
                List<Vector2Int> roadPixels = getDesignatedPixels(villageMap, designatedAreaTypes);
                float accessibilityInterestValue =
                    calcInterestFeature(    accessibility_tooClose_best_tooFar.x,
                                            accessibility_tooClose_best_tooFar.y,
                                            accessibility_tooClose_best_tooFar.z,
                                            newPointCoords,
                                            roadPixels
                                        );
                interestValues.Add(accessibilityInterestValue);
                
                //worship
                designatedAreaTypes = new VillagePixelType[] {VillagePixelType.ReligiousBuildingSeed};
                List<Vector2Int> reliSeeds = getDesignatedPixels(villageMap, designatedAreaTypes);
                float worshipInterestValue =
                    calcInterestFeature(    worship_tooClose_best_tooFar.x,
                                            worship_tooClose_best_tooFar.y,
                                            worship_tooClose_best_tooFar.z,
                                            newPointCoords,
                                            reliSeeds
                                        );
                interestValues.Add(worshipInterestValue);

                //fortification
                designatedAreaTypes = new VillagePixelType[] {VillagePixelType.FortificationSeed};
                List<Vector2Int> fortSeeds = getDesignatedPixels(villageMap, designatedAreaTypes);
                float fortificationInterestValue =
                    calcInterestFeature(    fortification_tooClose_best_tooFar.x,
                                            fortification_tooClose_best_tooFar.y,
                                            fortification_tooClose_best_tooFar.z,
                                            newPointCoords,
                                            fortSeeds
                                        );
                interestValues.Add(fortificationInterestValue);

                //geografical domination
                VillagePixelType[] soughtPixelTypes = new VillagePixelType[] {VillagePixelType.ReligiousBuildingSeed, VillagePixelType.FortificationSeed};
                Vector2Int closestPOI = getClosestPixelOfType(newPointCoords, soughtPixelTypes, villageMap);
                float distanceToPOI = Vector2Int.Distance(closestPOI, newPointCoords);
                float distanceImpact = Mathf.InverseLerp(0, geographicalDomination_maxDistanceToPOI, distanceToPOI);
                distanceImpact = 1-distanceImpact;

                float buildingHeight = getHeight(newPointCoords, terrainVertices, meshSize);
                float poiHeight = getHeight(closestPOI, terrainVertices, meshSize);
                float lowestPixelHeight = getLowestPixelHeight(villageMap);
                float heightImpact = Mathf.InverseLerp(lowestPixelHeight, poiHeight, buildingHeight);

                //modifier: [0.0f, 0.3f]
                float geoDominationModifier = 0.3f * heightImpact * distanceImpact;

                //interest function
                if(interestValues.Contains(0f)) {
                    return 0f;
                } else {
                    float resultInterestValue = 0f;
                    foreach(float interestValue in interestValues) {
                        resultInterestValue += interestValue;
                    }
                    resultInterestValue = Mathf.Max(0.2f, resultInterestValue/interestValues.Count);
                    resultInterestValue += geoDominationModifier;
                    return resultInterestValue;
                }

                float calcInterestFeature(float minDistanceValue, float bestDistanceValue, float maxDistanceValue, Vector2Int currentPixel, List<Vector2Int> pixelsOfInterest) {
                    float interestValue;
                    //if there are no pixelsOfInterest, pretend like the distance is optimal (ignore feature with default=bestDistanceValue)
                    float distToNearestPOI = calcLowestPixelDistance(currentPixel, pixelsOfInterest, bestDistanceValue);
                    if(distToNearestPOI < bestDistanceValue) {
                        interestValue = Mathf.InverseLerp(minDistanceValue, bestDistanceValue, distToNearestPOI);
                    } else {
                        interestValue = Mathf.InverseLerp(bestDistanceValue, maxDistanceValue, distToNearestPOI);
                        interestValue = 1-interestValue;
                    }
                    return interestValue;
                }
            }
        }

        float getLowestPixelHeight(VillagePixelType[,] villageMap) {
            float result = float.MaxValue;
            for(int y = 0; y < meshSize.y; y++) {
                for(int x = 0; x < meshSize.x; x++) {
                    if(villageMap[x, y] != VillagePixelType.OutsideArea) {
                        float vertexHeight = getHeight(new Vector2Int(x, y), terrainVertices, meshSize);
                        if(vertexHeight < result) {
                            result = vertexHeight;
                        }
                    }
                }
            }
            return result;
        }

        Vector2Int getClosestPixelOfTypeInDirection(Vector2Int currentPixel, Vector2Int coneDirection, VillagePixelType[] soughtPixelTypes, VillagePixelType[,] villageMap) {
            List<Vector2Int> soughtPixels = getDesignatedPixels(villageMap, soughtPixelTypes);
            Vector2Int closestSoughtPixel = new Vector2Int(-1, -1);
            float shortestDistance = float.MaxValue;
            foreach(Vector2Int soughtPixel in soughtPixels) {
                bool isPixelInCone = isPointInsideCone(soughtPixel, currentPixel, coneDirection, roadCycleAngle);
                if(isPixelInCone) {
                    float distanceToNewPoint = Vector2Int.Distance(currentPixel, soughtPixel);
                    if(distanceToNewPoint < shortestDistance) {
                        shortestDistance = distanceToNewPoint;
                        closestSoughtPixel = soughtPixel;
                    }
                }
            }
            return closestSoughtPixel;
        }

        //source: discussions.unity.com; Tortuap
        bool isPointInsideCone (Vector2Int point, Vector2Int coneOrigin, Vector2Int coneDirection, int maxAngle) {
            var pointDirection = point - coneOrigin;
            var angle = Vector2.Angle ( coneDirection, pointDirection );
            if ( angle < maxAngle )
                return true;
            return false;
        }

        Vector2Int getFarthestPixelOfType(Vector2Int currentPixel, VillagePixelType[] soughtPixelTypes, VillagePixelType[,] villageMap) {
            List<Vector2Int> soughtPixels = new List<Vector2Int>();
            foreach(VillagePixelType type in soughtPixelTypes) {
                VillagePixelType[] designatedAreaTypes = new VillagePixelType[] {type};
                soughtPixels.AddRange(getDesignatedPixels(villageMap, designatedAreaTypes));
            }

            Vector2Int farthestsoughtPixel = new Vector2Int(-1, -1);
            float farthestDistance = -1f;
            foreach(Vector2Int soughtPixel in soughtPixels) {
                float distanceToNewPoint = Vector2Int.Distance(currentPixel, soughtPixel);
                if(distanceToNewPoint > farthestDistance) {
                    farthestDistance = distanceToNewPoint;
                    farthestsoughtPixel = soughtPixel;
                }
            }
            
            return farthestsoughtPixel;
        }

        Dictionary<Vector2Int, float> getXHighestPointsInArea(List<Vector2Int> area, int amountOfPoints) {
            Dictionary<Vector2Int, float> highestPoints = new Dictionary<Vector2Int, float>();
            foreach(Vector2Int point in area) {
                float pointHeight = getHeight(point, terrainVertices, meshSize);
                if(highestPoints.Count < amountOfPoints) {
                    highestPoints.Add(point, pointHeight);
                } else {
                    KeyValuePair<Vector2Int, float> lowestOfTheHighPoint;
                    bool firstIteration = true;
                    foreach(KeyValuePair<Vector2Int, float> highPoint in highestPoints) {
                        if(firstIteration) {
                            lowestOfTheHighPoint = highPoint;
                        } else if(highPoint.Value < lowestOfTheHighPoint.Value) {
                            lowestOfTheHighPoint = highPoint;
                        }
                        firstIteration = false;
                    }
                    if(lowestOfTheHighPoint.Value < pointHeight) {
                        highestPoints.Remove(lowestOfTheHighPoint.Key);
                        highestPoints.Add(point, pointHeight);
                    }
                }
            }

            return highestPoints;
        }

        //grow village area from seed
        void calcVillageArea() {
            VillagePixelType[,] oldVillageAreaMap;
            villageVertexCount = 0;
            Vector2Int[] neighborhoodCoords;
            for(int i = 0; i < villageRadius; i++) {
                if(i > villageRadius/2) {
                    neighborhoodCoords = fourNeighborhoodCoords;
                } else {
                    neighborhoodCoords = eightNeighborhoodCoords;
                }

                oldVillageAreaMap = copyMap(resultVillageAreaMap);
                for(int y = 0; y < meshSize.y; y++) {
                    for(int x = 0; x < meshSize.x; x++) {

                        if(oldVillageAreaMap[x,y] == VillagePixelType.StandardVillageArea) {
                            foreach(Vector2Int offset in neighborhoodCoords) {
                                Vector2Int neighborPixel = new Vector2Int(x + offset.x, y + offset.y);
                                if(isPixelInTexture(neighborPixel, meshSize, 5)) {
                                    float neighborSteepness = steepnessMap.GetPixel(neighborPixel.x, neighborPixel.y).r;
                                    int neighborWaterDistance = waterDistanceMap[neighborPixel.x, neighborPixel.y];

                                    if( resultVillageAreaMap[neighborPixel.x, neighborPixel.y] == VillagePixelType.OutsideArea &&
                                        neighborSteepness >= possibleSteepness_min_max.x && neighborSteepness <= possibleSteepness_min_max.y &&
                                        ((neighborWaterDistance >= possibleWaterDistance_min_max.x && neighborWaterDistance <= possibleWaterDistance_min_max.y) || !waterExists)
                                        ) {
                                            if(i < villageRadius-1) {
                                                resultVillageAreaMap[neighborPixel.x, neighborPixel.y] = VillagePixelType.StandardVillageArea;
                                            } else {
                                                resultVillageAreaMap[neighborPixel.x, neighborPixel.y] = VillagePixelType.VillageBorder;
                                                villageBorderPixelCount++;
                                            }
                                            villageVertexCount++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        //find seed that satisfies construction criteria
        bool calcSeedCoords() {
            bool noSeedFound = true;
            Vector2Int seedCoordinates = new Vector2Int(0,0);
            int seedIteration = 0;
            while(noSeedFound && seedIteration < maxIterations) {
                seedCoordinates = new Vector2Int(Random.Range(5, meshSize.x-5), Random.Range(5, meshSize.y-5));
                float seedSteepness = steepnessMap.GetPixel(seedCoordinates.x, seedCoordinates.y).r;
                int seedWaterDistance = waterDistanceMap[seedCoordinates.x, seedCoordinates.y];

                if( seedSteepness >= possibleSteepness_min_max.x && seedSteepness <= possibleSteepness_min_max.y &&
                    ((seedWaterDistance >= possibleSteepness_min_max.x && seedWaterDistance <= possibleWaterDistance_min_max.y) || !waterExists)
                    ) {
                        resultVillageAreaMap[seedCoordinates.x, seedCoordinates.y] = VillagePixelType.StandardVillageArea;
                        noSeedFound = false;
                }
                
                seedIteration++;
                if(seedIteration >= maxIterations) {
                    Debug.Log("No village seed found in " + maxIterations + " iterations.");
                    return false;
                }
            }
            return true;
        }

        float calcLowestPixelDistance(Vector2Int newPoint, List<Vector2Int> existingPoints, float defaultValue) {
            float lowestPixelDistance;
            if(existingPoints.Count==0) {
                lowestPixelDistance = defaultValue;
            } else {
                lowestPixelDistance = float.MaxValue;
                foreach(Vector2Int existingPoint in existingPoints) {
                    float currentPixelDistance = Vector2Int.Distance(existingPoint, newPoint);
                    if(currentPixelDistance < lowestPixelDistance) {
                        lowestPixelDistance = currentPixelDistance;
                    }
                }
            }
            return lowestPixelDistance;
        }
    }

    Vector2Int getClosestPixelOfType(Vector2Int currentPixel, VillagePixelType[] soughtPixelTypes, VillagePixelType[,] villageMap) {
        List<Vector2Int> soughtPixels = getDesignatedPixels(villageMap, soughtPixelTypes);
        Vector2Int closestSoughtPixel = new Vector2Int(-1, -1);
        float shortestDistance = float.MaxValue;
        foreach(Vector2Int soughtPixel in soughtPixels) {
            float distanceToNewPoint = Vector2Int.Distance(currentPixel, soughtPixel);
            if(distanceToNewPoint < shortestDistance) {
                shortestDistance = distanceToNewPoint;
                closestSoughtPixel = soughtPixel;
            }
        }
        
        return closestSoughtPixel;
    }

    List<Vector2Int> getDesignatedPixels(VillagePixelType[,] villageMap, VillagePixelType[] designatedAreaTypes) {
        //determine coords of all designatedAreaType-pixels
        List<Vector2Int> designatedAreaPixels = new List<Vector2Int>();
        for(int y = 0; y < villageMap.GetLength(1); y++) {
            for(int x = 0; x < villageMap.GetLength(0); x++) {
                if(designatedAreaTypes.Contains(villageMap[x,y])) {
                    designatedAreaPixels.Add(new Vector2Int(x, y));
                }
            }
        }
        return designatedAreaPixels;
    }

    //calculate best (shortest+easiest) road from source to destination with dijkstra's algo
    List<Vector2Int> calcShortestRoad(Vector2Int source, Vector2Int dest, VillagePixelType[,] villageMap, Texture2D steepnessMap, int[,] waterMap, float roadWeight, VillagePixelType[] designatedAreaTypes) {
        List<Vector2Int> pixelToSearch = getDesignatedPixels(villageMap, designatedAreaTypes);
        if(!pixelToSearch.Contains(source)) {
            pixelToSearch.Add(source);
        }
        if(!pixelToSearch.Contains(dest)) {
            pixelToSearch.Add(dest);
        }

        List<float> distances = new List<float>();
        List<Vector2Int> previousPixels = new List<Vector2Int>();
        List<Vector2Int> unvisitedPixels = new List<Vector2Int>();

        for(int i=0; i < pixelToSearch.Count; i++) {
            distances.Add(float.MaxValue);
            previousPixels.Add(new Vector2Int(-1, -1));
            unvisitedPixels.Add(pixelToSearch[i]);
        }
        distances[pixelToSearch.IndexOf(source)] = 0;

        while(unvisitedPixels.Count > 0 && unvisitedPixels.Contains(dest)) {
            float smallestDist = -1;
            Vector2Int closestUnvisitedPixel = new Vector2Int(-1, -1);
            foreach(Vector2Int unvisitedPixel in unvisitedPixels) {
                float pixelDist = distances[pixelToSearch.IndexOf(unvisitedPixel)];
                if(smallestDist == -1) {
                    smallestDist = pixelDist;
                    closestUnvisitedPixel = unvisitedPixel;
                } else if(pixelDist < smallestDist) {
                    smallestDist = pixelDist;
                    closestUnvisitedPixel = unvisitedPixel;
                }
            }

            Vector2Int currentPixel = closestUnvisitedPixel;
            unvisitedPixels.Remove(currentPixel);

            foreach(Vector2Int neighborOffset in eightNeighborhoodCoords) {
                Vector2Int neighborPixel = new Vector2Int(currentPixel.x+neighborOffset.x, currentPixel.y+neighborOffset.y);
                if(unvisitedPixels.Contains(neighborPixel)) {

                    //weight the distance to neighbors with slope and existing roads
                    float distToNeighbor;
                    if(waterMap[neighborPixel.x,neighborPixel.y] == 0) {
                        distToNeighbor = float.MaxValue;
                    } else {
                        if(villageMap[neighborPixel.x, neighborPixel.y] == VillagePixelType.Road) {
                            distToNeighbor = smallestDist + roadWeight;
                        } else {
                            float steepness = steepnessMap.GetPixel(neighborPixel.x, neighborPixel.y).r;

                            if(steepness < possibleSteepness_min_max.x || steepness > possibleSteepness_min_max.y)
                                steepness = float.MaxValue;
                            distToNeighbor = smallestDist + steepness;
                        }
                    }

                    int neighborIndex = pixelToSearch.IndexOf(neighborPixel);
                    if(distToNeighbor < distances[neighborIndex]) {
                        distances[neighborIndex] = distToNeighbor;
                        previousPixels[neighborIndex] = currentPixel;
                    }
                }
            }
        }

        List<Vector2Int> shortestPath = new List<Vector2Int>();
        shortestPath.Add(dest);
        bool sourceBackTracked = false;

        while(!sourceBackTracked) {
            Vector2Int prevPixel = shortestPath[shortestPath.Count-1];
            Vector2Int nextPixel = previousPixels[pixelToSearch.IndexOf(prevPixel)];
            if(nextPixel == new Vector2Int(-1, -1)) {
                Debug.Log("No Path from " + source + " to " + dest + " was found. No road was build in this step.");
                return new List<Vector2Int>();
            } else {
                shortestPath.Add(nextPixel);
            }

            if(shortestPath.Contains(source)) {
                sourceBackTracked = true;
            }
        }
        shortestPath.Reverse();
        shortestPath.Remove(source);
        shortestPath.Remove(dest);
        return shortestPath;
    }

    public T[,] copyMap<T>(T[,] map) {
        T[,] villageMap = new T[map.GetLength(0), map.GetLength(1)];
        for(int y = 0; y < map.GetLength(1); y++) {
            for(int x = 0; x < map.GetLength(0); x++) {
                villageMap[x,y] = map[x,y];
            }
        }
        return villageMap;
    }

    private Texture2D generateWaterMap() {
        Vector3[] terrainVertices = terrainGenerator.getUncondensedTerrainVertices();
        float maxHeight = terrainGenerator.getMaxTerrainheight();
		float minHeight = terrainGenerator.getMinTerrainheight();
		float relativeWaterHeight = terrainGenerator.relativeWaterHeight;
		float absoluteWaterHeight = Mathf.Lerp(minHeight, maxHeight, relativeWaterHeight);
        Vector2Int meshSize = terrainGenerator.getUncondensedMeshSize();
        int[,] waterDistances = initMap(meshSize, -1);

        int currentWaterDistance = 0;
        bool somePixelsNotSet = true;
        int maxWaterDistance = -1;
        while(somePixelsNotSet) {
            somePixelsNotSet = false;

            for(int y = 0; y < meshSize.y; y++) {
                for(int x = 0; x < meshSize.x; x++) {

                    if(currentWaterDistance == 0 && getHeight(new Vector2Int(x,y), terrainVertices, meshSize) < absoluteWaterHeight) {
                        waterDistances[x,y] = 0;
                        maxWaterDistance = 0;
                        somePixelsNotSet = true;
                        
                    } else if(currentWaterDistance > 0 && waterDistances[x,y] == currentWaterDistance-1) {
                        foreach(Vector2Int coords in eightNeighborhoodCoords) {
                            Vector2Int neighborPixel = new Vector2Int(x + coords.x, y + coords.y);
                            if(isPixelInTexture(neighborPixel, meshSize, 0) && waterDistances[neighborPixel.x, neighborPixel.y] == -1) {
                                waterDistances[neighborPixel.x, neighborPixel.y] = currentWaterDistance;
                                if(currentWaterDistance > maxWaterDistance) {
                                    maxWaterDistance = currentWaterDistance;
                                }
                                somePixelsNotSet = true;
                            }
                        }
                    }

                }
            }

            currentWaterDistance++;
        }
        waterDistanceMap = waterDistances;

        Texture2D resultTex = new Texture2D(meshSize.x, meshSize.y);
		resultTex.filterMode = FilterMode.Point;
        for(int y = 0; y < meshSize.y; y++) {
            for(int x = 0; x < meshSize.x; x++) {
                float pixelValue;
                if(maxWaterDistance > -1) {
                    pixelValue = Mathf.InverseLerp(0, maxWaterDistance, waterDistances[x,y]);
                } else {
                    pixelValue = 1f;
                }
                resultTex.SetPixel(x,y, new Color(pixelValue, 0, 0, 1));
            }
        }

		return resultTex;
    }

    float getHeight(Vector2Int coords, Vector3[] terrainVertices, Vector2Int meshSize) {
        int index = meshSize.x * coords.y + coords.x;
        return terrainVertices[index].y;
    }

    T[,] initMap<T>(Vector2Int mapSize, T defaultValue) {
        T[,] map = new T[mapSize.x, mapSize.y];
        for(int y = 0; y < mapSize.y; y++) {
            for(int x = 0; x < mapSize.x; x++) {
                map[x,y] = defaultValue;
            }
        }
        return map;
    }

    bool isPixelInTexture(Vector2Int pixel, Vector2Int texSize, int offset) {
		return (pixel.x >= 0+offset && pixel.x < texSize.x-offset && pixel.y >= 0+offset && pixel.y < texSize.y-offset);
	}

    List<int> randomlyOrderNumbers(List<int> numbers) {
        List<int> resultList = new List<int>();
        int iterations = numbers.Count;
        for(int i = 0; i < iterations; i++) {
            int randomIndex = Random.Range(0, numbers.Count);
            resultList.Add(numbers[randomIndex]);
            numbers.RemoveAt(randomIndex);
        }

        return resultList;
    }

    private bool hasRoadNeighbor(VillagePixelType[,] villageMap, Vector2Int pixel) {
        foreach(Vector2Int neighborOffset in eightNeighborhoodCoords) {
            Vector2Int neighbor = new Vector2Int(pixel.x+neighborOffset.x, pixel.y+neighborOffset.y);
            if( isPixelInTexture(neighbor, new Vector2Int(villageMap.GetLength(0), villageMap.GetLength(1)), 0) &&
                villageMap[neighbor.x, neighbor.y] == VillagePixelType.Road) {
                return true;
            }
        }
        return false;
    }

    private static void SaveTextureAsPNG(Texture2D _texture, string _fullPath) {
		byte[] _bytes =_texture.EncodeToPNG();
		System.IO.File.WriteAllBytes(_fullPath, _bytes);
	}

    public VillagePixelType[,] getVillageMap() {
        return villageMap;
    }

    public bool isVillageMapGenerated() {
        return villageMapGenerated;
    }
}
