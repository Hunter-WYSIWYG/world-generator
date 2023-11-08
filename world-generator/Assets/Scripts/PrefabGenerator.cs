using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PrefabGenerator : MonoBehaviour
{
    [System.Serializable]
    public class PrefabData {
        //frequency of prefab = placementFrequency / sum(PrefabSet-frequencies)
        public int placementFrequency;
        public GameObject prefab;
    }

    [System.Serializable]
    public class PrefabSet {
        public string name;
        //added to base prefab frequency; if 0: disable prefabs
        public int prefabDensityPenalty;
        public PrefabData[] prefabData;
    }

    public bool updatePrefabs = false;
    public bool removePrefabs = false;

    [Header("Village Parameter")]
    public float villageSizeScaling;
    public GameObject[] villagePrefabs;

    [Header("Vegetation Parameter")]
    public float noiseDensity;
    public Vector2 noiseOffset;
    [Range(0f,1.5f)]
    public float forestSize;
    [Range(0f,0.5f)]
    public float forestSpread;
    [Range(1,10)]
    //place a vegetation prefab every [forestDensity] vertices
    public int forestDensity;
    [Range(0f,1f)]
    public float steepnessInfluence;
    [Range(0f,1f)]
    public float relativeTreeHeightLimit;
    public float vegetationSizeScaling;
    public PrefabSet[] vegetationSets;

    [Header("Script References")]
    public TerrainGenerator terrainGenerator;
    public BiomeGenerator biomeGenerator;
    public VillageGenerator villageGenerator;

    private Texture2D noiseMap;
    private Vector2Int uncondensedMeshSize;
    private Vector3[] uncondensedTerrainVertices;
    private ArrayList currentTrees = new ArrayList();
    private ArrayList currentBuildings = new ArrayList();
    private Texture2D steepnessMap;

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

    void Start()
    {
        updatePrefabs = false;
        removePrefabs = false;
        currentTrees = new ArrayList();
        currentBuildings = new ArrayList();
    }

    void Update()
    {
        if(updatePrefabs && terrainGenerator.isMeshGenerated()) {
            updatePrefabs = false;

            uncondensedMeshSize = terrainGenerator.getUncondensedMeshSize();
            uncondensedTerrainVertices = terrainGenerator.getUncondensedTerrainVertices();
            steepnessMap = biomeGenerator.getSteepnessMap();

            if(villageGenerator.isVillageMapGenerated()) {

                prefabCleanUp(currentBuildings);
                currentBuildings = placeVillageOnMesh(uncondensedTerrainVertices, villageGenerator.getVillageMap());
            } else {
                Debug.Log("No village map generated to instantiate the village prefabs");
            }

            noiseMap = generateNoiseMap(noiseDensity, noiseOffset);
            prefabCleanUp(currentTrees);
            currentTrees = placeVegetationOnMesh(uncondensedTerrainVertices, noiseMap, villageGenerator.getVillageMap());
            noiseMap = generateNoiseMap(noiseDensity, noiseOffset);
            prefabCleanUp(currentTrees);
            currentTrees = placeVegetationOnMesh(uncondensedTerrainVertices, noiseMap, villageGenerator.getVillageMap());
        }

        if(removePrefabs) {
            removePrefabs = false;
            prefabCleanUp(currentBuildings);
            prefabCleanUp(currentTrees);
        }
    }

    private ArrayList placeVillageOnMesh(Vector3[] meshNodes, VillageGenerator.VillagePixelType[,] villageMap) {
        ArrayList buildingArray = new ArrayList();

        for(int y = 0; y < villageMap.GetLength(1); y++) {
			for(int x = 0; x < villageMap.GetLength(0); x++) {

                switch(villageMap[x,y]) {
                    case VillageGenerator.VillagePixelType.HouseSeed:
                        placeVillagePrefab(buildingArray, villagePrefabs[0], new Vector2Int(x,y));
                        break;
                    case VillageGenerator.VillagePixelType.ReligiousBuildingSeed:
                        placeVillagePrefab(buildingArray, villagePrefabs[1], new Vector2Int(x,y));
                        break;
                    case VillageGenerator.VillagePixelType.FortificationSeed:
                        placeVillagePrefab(buildingArray, villagePrefabs[2], new Vector2Int(x,y));
                        break;
                }
            }
        }
        return buildingArray;

        Vector2Int getClosestStreetNeighbor(Vector2Int coords) {
            Vector2Int closestNeighboringStreetPixel = coords;
            float distanceToNearestPixel = float.MaxValue;
            foreach(Vector2Int offset in eightNeighborhoodCoords) {
                Vector2Int neighborPixel = new Vector2Int(coords.x + offset.x, coords.y + offset.y);
                float distanceToNeighbor = Vector2Int.Distance(coords, neighborPixel);
                if( isPixelInTexture(neighborPixel, terrainGenerator.getMeshSize()) &&
                    villageMap[neighborPixel.x,neighborPixel.y] == VillageGenerator.VillagePixelType.Road &&
                    distanceToNeighbor < distanceToNearestPixel
                ) {
                    distanceToNearestPixel = distanceToNeighbor;
                    closestNeighboringStreetPixel = neighborPixel;
                }
            }
            return closestNeighboringStreetPixel;
        }

        void placeVillagePrefab(ArrayList buildings, GameObject prefab, Vector2Int coords) {
            int meshIndex = getMeshIndex(coords.x, coords.y);

            Vector2Int closestStreetNeighbor = getClosestStreetNeighbor(coords);
            Vector3 startVector = meshNodes[getMeshIndex(coords.x, coords.y)];
            Vector3 endVector = meshNodes[getMeshIndex(closestStreetNeighbor.x, closestStreetNeighbor.y)];
            Vector3 eulerRotation = Quaternion.LookRotation(endVector - startVector).eulerAngles;
            eulerRotation.x = eulerRotation.z = 0;
            eulerRotation.y = eulerRotation.y - 180;
            Quaternion rotation = Quaternion.Euler(eulerRotation);

            Vector3 position = meshNodes[meshIndex];
            GameObject building = Instantiate(prefab, position, rotation);
            Vector3 prefabScale = building.transform.localScale;
            building.transform.localScale = new Vector3(prefabScale.x*villageSizeScaling, prefabScale.y*villageSizeScaling, prefabScale.z*villageSizeScaling);
            buildings.Add(building);
        }

        int getMeshIndex(int x, int y) {
            return villageMap.GetLength(0) * y + x;
        }
    }

    private Texture2D generateNoiseMap(float noiseDensity, Vector2 noiseOffset) {
        Texture2D resultTexture = new Texture2D(uncondensedMeshSize.x, uncondensedMeshSize.y);
		resultTexture.filterMode = FilterMode.Point;
        for(int y = 0; y < resultTexture.height; y++) {
			for(int x = 0; x < resultTexture.width; x++) {
                float pixelValue = calcNoisePixel(new Vector2(x,y), noiseDensity, noiseOffset);
                resultTexture.SetPixel(x,y,new Color(pixelValue, 0, 0, 1));
			}
		}
		return resultTexture;

        float calcNoisePixel(Vector2 coords,float noiseDensity, Vector2 noiseOffset) {
            float noiseX = coords.x * noiseDensity + noiseOffset.x;
            float noiseY = coords.y * noiseDensity + noiseOffset.y;
            float pixelValue = Mathf.Clamp(Mathf.PerlinNoise(noiseX, noiseY), 0f, 1f);
            return pixelValue;
        }
    }

    private ArrayList placeVegetationOnMesh(Vector3[] meshNodes, Texture2D placementMap, VillageGenerator.VillagePixelType[,] villageMap) {
        ArrayList treeArray = new ArrayList();
        float vertexDistance = terrainGenerator.terrainScaling;
        
        int meshIndex;
        for(int y = 0; y < placementMap.height; y++) {
			for(int x = 0; x < placementMap.width; x++) {
                //what kind of tree to place?
                Vector2Int biomeID = chooseBiomeID(new Vector2Int(x,y));
                PrefabSet PrefabSet = vegetationSets[biomeID.x * 3 + biomeID.y];

                int forestDensity = calcLocalForestDensity(PrefabSet.prefabDensityPenalty);

                //potentially thin out forest
                int randomVertexOffsetX = Random.Range(0, forestDensity-1);
                int randomVertexOffsetY = Random.Range(0, forestDensity-1);

                int vCoordX = x+randomVertexOffsetX;
                int vCoordY = y+randomVertexOffsetY;

                //should tree be placed?
                if( isPixelInTexture(new Vector2Int(vCoordX, vCoordY), new Vector2Int(placementMap.width, placementMap.height))
                    && x % forestDensity == 0
                    && y % forestDensity == 0
                    && getFitness(placementMap,new Vector2Int(vCoordX, vCoordY)) > 1.5f - forestSize) {

                    //calc local tree offset
                    float randomTreeOffsetX = Random.Range(-0.5f, 0.5f);
                    float randomTreeOffsetZ = Random.Range(-0.5f, 0.5f);

                    Vector2Int offsetCoordinates = getVertexOffsets(randomTreeOffsetX, randomTreeOffsetZ);
                    float currentHeight = getHeight(vCoordX,vCoordY);
                    float prefabHeight;

                    if( vCoordX+offsetCoordinates.x > 0
                        && vCoordY+offsetCoordinates.y > 0
                        && vCoordX+offsetCoordinates.x < placementMap.width-1
                        && vCoordY+offsetCoordinates.y < placementMap.height-1) {

                        //approximate height of the offset prefab
                        float neighborHeight = getHeight(vCoordX+offsetCoordinates.x, vCoordY+offsetCoordinates.y);
                        float lerpValue = Mathf.Max(Mathf.Abs(randomTreeOffsetX), Mathf.Abs(randomTreeOffsetZ));
                        prefabHeight = Mathf.Lerp(currentHeight, neighborHeight, lerpValue);
                    } else {
                        prefabHeight = currentHeight;
                    }
                    //lower prefab-height by 2% of the vertex distance to avoid some slightly floating prefabs
                    prefabHeight -= vertexDistance*0.02f;

                    //set prefab position in world coordniates
                    randomTreeOffsetX *= vertexDistance;
                    randomTreeOffsetZ *= vertexDistance;
                    meshIndex = placementMap.width * vCoordY + vCoordX;
                    Vector3 treePosition = new Vector3(meshNodes[meshIndex].x + randomTreeOffsetX, prefabHeight, meshNodes[meshIndex].z + randomTreeOffsetZ);
                    Vector3 unscaledTreePosition = treePosition / vertexDistance;

                    float minHeight = terrainGenerator.getMinTerrainheight();
                    float maxHeight = terrainGenerator.getMaxTerrainheight();
                    float absoluteWaterHeight = Mathf.Lerp(minHeight, maxHeight, terrainGenerator.relativeWaterHeight);
                    if(isPosInMap(treePosition) && treePosition.y > absoluteWaterHeight && !isPositionBlocked(unscaledTreePosition)) {
                        PrefabData[] possiblePrefabs = PrefabSet.prefabData;
                        if(possiblePrefabs.Length > 0) {
                            int prefabID = rollPrefabID(possiblePrefabs);
                            GameObject prefabToUse = possiblePrefabs[prefabID].prefab;
                            Quaternion randomRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                            GameObject tree = Instantiate(prefabToUse, treePosition, randomRotation);
                            Vector3 prefabScale = tree.transform.localScale;
                            tree.transform.localScale = new Vector3(prefabScale.x*vegetationSizeScaling, prefabScale.y*vegetationSizeScaling, prefabScale.z*vegetationSizeScaling);
                            treeArray.Add(tree);
                        }
                    }
                }
            }
        }
        return treeArray;

        bool isPositionBlocked(Vector3 treePosition) {
            Vector2Int villageMapCoords = new Vector2Int(Mathf.RoundToInt(treePosition.x),Mathf.RoundToInt(treePosition.z));
            if(containsVillageStructure(villageMapCoords.x, villageMapCoords.y)) {
                return true;
            }
            
            foreach(Vector2Int offset in eightNeighborhoodCoords) {
                Vector2Int neighborPixel = new Vector2Int(villageMapCoords.x + offset.x, villageMapCoords.y + offset.y);
                if(containsVillageStructure(neighborPixel.x, neighborPixel.y)) {
                    return true;
                }
            }

            return false;

            bool containsVillageStructure(int x, int y) {
                if(isPixelInTexture(new Vector2Int(x,y), new Vector2Int(villageMap.GetLength(0), villageMap.GetLength(1)))) {
                    if( villageMap[x,y] == VillageGenerator.VillagePixelType.VillageRoadEntry ||
                        villageMap[x,y] == VillageGenerator.VillagePixelType.FortificationSeed ||
                        villageMap[x,y] == VillageGenerator.VillagePixelType.HouseSeed ||
                        villageMap[x,y] == VillageGenerator.VillagePixelType.ReligiousBuildingSeed ||
                        villageMap[x,y] == VillageGenerator.VillagePixelType.Road ||
                        villageMap[x,y] == VillageGenerator.VillagePixelType.MapRoadEntry
                    ) {
                        return true;
                    }
                }
                return false;
            }
        }

        //determine vertex neighbor in which direction the prefab is set off
        //pre-condition: offsetX & offsetY = random [-0.5,0.5]
        Vector2Int getVertexOffsets(float offsetX, float offsetY) {
            return new Vector2Int(calcVertexOffset(offsetX), calcVertexOffset(offsetY));

            int calcVertexOffset(float offset) {
                if(offset < -0.1666f) {
                    return -1;
                } else if(offset > 0.1666f) {
                    return 1;
                }
                return 0;
            }
        }
    }

    //pre-condition: prefabData can not be empty
    private int rollPrefabID(PrefabData[] prefabData) {
        int frequencySum = 0;
        foreach(PrefabData data in prefabData) {
            frequencySum += data.placementFrequency;
        }
        int dieResult = Random.Range(0,frequencySum-1);

        int checkSum = 0;
        int index = 0;
        foreach(PrefabData data in prefabData) {
            if(dieResult >= checkSum && dieResult < checkSum + data.placementFrequency) {
                return index;
            }
            checkSum += data.placementFrequency;
            index++;
        }

        //should never return this
        return -1;
    }

    private int calcLocalForestDensity(int penalty) {
        int maxMapSize = Mathf.Max(uncondensedMeshSize.x, uncondensedMeshSize.y);

        if(penalty < 0) {
            penalty = maxMapSize;
        }

        return forestDensity + penalty;
    }

    //map splatmaps into comprehensible format: (tempID, precID)
    //determine biome for the blended areas with more than 1 possible biome
    private Vector2Int chooseBiomeID(Vector2Int pos) {
        //first dim: temp intensity values; second dim: prec intensity values
        //why float: blended biome borders are used
        float[,] biomeData = new float[3,3];

        float combinedPixelValue = 0;
        int index = 0;
        foreach(Texture2D splatmap in biomeGenerator.getBiomeSplatmaps()) {
            biomeData[index, 0] = splatmap.GetPixel(pos.x, pos.y).r;
            biomeData[index, 1] = splatmap.GetPixel(pos.x, pos.y).g;
            biomeData[index, 2] = splatmap.GetPixel(pos.x, pos.y).b;
            combinedPixelValue += biomeData[index, 0];
            combinedPixelValue += biomeData[index, 1];
            combinedPixelValue += biomeData[index, 2];
            index++;
        }

        //determine which offset to use at random
        //propabilities follow the ratio of the biome values
        float randomValue = Random.Range(0,combinedPixelValue);
        float floorCheckValue = 0;
        for(int temp=0; temp<biomeData.GetLength(0); temp++) {
            for(int prec=0; prec<biomeData.GetLength(1); prec++) {
                float biomeDataValue = biomeData[temp,prec];
                if(biomeDataValue > 0) {
                    if(randomValue >= floorCheckValue && randomValue <= floorCheckValue+biomeDataValue) {
                        return new Vector2Int(temp,prec);
                    } else {
                        floorCheckValue += biomeDataValue;
                    }
                }
            }
        }

        //default
        return new Vector2Int(1,1);
    }

    private float getFitness(Texture2D placementMap, Vector2Int pos) {
        float fitness = placementMap.GetPixel(pos.x, pos.y).r;
        fitness += Random.Range(-forestSpread, forestSpread);

        if(steepnessMap.GetPixel(pos.x, pos.y).r > 1 - steepnessInfluence) {
            fitness -= 0.7f;
        }

        float maxHeight = terrainGenerator.getMaxTerrainheight();
		float minHeight = terrainGenerator.getMinTerrainheight();
		float maxTreeHeight = maxHeight - ((maxHeight - minHeight) * (1-relativeTreeHeightLimit));
        if(getHeight(pos.x, pos.y) > maxTreeHeight) {
            fitness -= 0.7f;
        }

        return fitness;
    }

    private float getHeight(int x, int y) {
        int index = uncondensedMeshSize.x * y + x;
        return uncondensedTerrainVertices[index].y;
    }

    private void prefabCleanUp(ArrayList prefabArray) {
        foreach(GameObject prefab in prefabArray) {
            Destroy(prefab);
        }
    }

    //world coordinates
    private bool isPosInMap(Vector3 pos) {
        float terrainScaling = terrainGenerator.terrainScaling;
        float mapSizeX = (uncondensedMeshSize.x-1) * terrainScaling;
        float mapSizeZ = (uncondensedMeshSize.y-1) * terrainScaling;
		return (pos.x >= terrainGenerator.terrainScaling && pos.x < mapSizeX-terrainScaling && pos.z >= terrainGenerator.terrainScaling && pos.z < mapSizeZ-terrainScaling);
	}

    //tex coordinates
    bool isPixelInTexture(Vector2Int pixel, Vector2Int texSize) {
		return (pixel.x >= 0 && pixel.x < texSize.x && pixel.y >= 0 && pixel.y < texSize.y);
	}
}
