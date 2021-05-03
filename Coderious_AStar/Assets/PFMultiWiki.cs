using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

public class PFMultiWiki : MonoBehaviour
{
    public struct Node
    {
        public int2 coord;
        public int2 parent;
        public float gScore;
        public float hScore;
        public float fScore;
    }

    Hashtable obstacles;
    Dictionary<int2, Node> starts;
    Node end;
    int safeGuard = 1000;

    public Tilemap map;
    public Tile defaultTile;
    public Camera cam;

    float unitSpeed = 5f;
    public GameObject tilePreFab;


    // Start is called before the first frame update
    void Start()
    {
        obstacles = new Hashtable();
        starts = new Dictionary<int2, Node>();
        end = new Node { coord = int2.zero, parent = int2.zero, gScore = float.MaxValue, hScore = float.MaxValue };
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButtonDown(0))
        {
            PlaceStart();
        }

        if (Input.GetKey(KeyCode.LeftControl) && Input.GetMouseButtonDown(0))
        {
            PlaceEnd();
        }

        if (Input.GetMouseButtonDown(0) &&
            !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.LeftControl))
        {
            PlaceObstacle();
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            ClearTiles();

            float startTime = Time.realtimeSinceStartup;

            FindPath();

            float endTime = Time.realtimeSinceStartup;
            Debug.Log(endTime - startTime);
        }

    }

    void ClearTiles()
    {
        map.ClearAllTiles();

        foreach (int2 s in starts.Keys)
        {
            Vector3Int start = new Vector3Int(s.x, s.y, 0);
            map.SetTile(start, defaultTile);
            map.SetTileFlags(start, TileFlags.None);
            map.SetColor(start, Color.green);
        }

        Vector3Int _end = new Vector3Int(end.coord.x, end.coord.y, 0);
        map.SetTile(_end, defaultTile);
        map.SetTileFlags(_end, TileFlags.None);
        map.SetColor(_end, Color.red);

        foreach (int2 o in obstacles.Keys)
        {
            Vector3Int obstacle = new Vector3Int(o.x, o.y, 0);
            map.SetTile(obstacle, defaultTile);
            map.SetTileFlags(obstacle, TileFlags.None);
            map.SetColor(obstacle, Color.black);
        }
    }

    void PlaceStart()
    {
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int mouseCell = map.WorldToCell(mouseWorldPos);
        int2 coord = new int2 { x = mouseCell.x, y = mouseCell.y };

        if (starts.ContainsKey(coord) && !coord.Equals(end.coord))
        {
            map.SetTile(new Vector3Int(coord.x, coord.y, 0), null);
            starts.Remove(coord);
        }
        else if (!obstacles.ContainsKey(coord) && !coord.Equals(end.coord))
        {
            Node startNode = new Node { coord = coord, parent = int2.zero, gScore = float.MaxValue, hScore = float.MaxValue };

            starts.Add(coord, startNode);
            map.SetTile(mouseCell, defaultTile);
            map.SetTileFlags(mouseCell, TileFlags.None);
            map.SetColor(mouseCell, Color.green);
        }
    }

    void PlaceEnd()
    {
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int mouseCell = map.WorldToCell(mouseWorldPos);
        int2 coord = new int2 { x = mouseCell.x, y = mouseCell.y };

        if (!obstacles.ContainsKey(coord) && !starts.ContainsKey(coord))
        {
            map.SetTile(new Vector3Int(end.coord.x, end.coord.y, 0), null);

            end.coord = coord;
            map.SetTile(mouseCell, defaultTile);
            map.SetTileFlags(mouseCell, TileFlags.None);
            map.SetColor(mouseCell, Color.red);
        }
    }

    void PlaceObstacle()
    {
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int mouseCell = map.WorldToCell(mouseWorldPos);
        int2 coord = new int2 { x = mouseCell.x, y = mouseCell.y };

        if (obstacles.ContainsKey(coord))
        {
            map.SetTile(new Vector3Int(coord.x, coord.y, 0), null);
            obstacles.Remove(coord);
        }
        else if (!starts.ContainsKey(coord) && !coord.Equals(end.coord))
        {
            obstacles.Add(coord, true);
            map.SetTile(mouseCell, defaultTile);
            map.SetTileFlags(mouseCell, TileFlags.None);
            map.SetColor(mouseCell, Color.black);
        }
    }

    public void FindPath()
    {
        NativeHashMap<int2, bool> isObstacle =
            new NativeHashMap<int2, bool>(obstacles.Count, Allocator.TempJob);
        NativeArray<int2> offsets = new NativeArray<int2>(8, Allocator.TempJob);
        NativeArray<Node> nativeStarts =
            new NativeArray<Node>(starts.Count, Allocator.TempJob);
        NativeMultiHashMap<int2, Node> resultList = new NativeMultiHashMap<int2, Node>((starts.Count) * safeGuard, Allocator.TempJob);

        foreach (int2 o in obstacles.Keys)
        {
            isObstacle.Add(o, true);
        }

        int counter = 0;

        foreach (Node n in starts.Values)
        {
            nativeStarts[counter] = n;
            counter++;
        }

        offsets[0] = new int2(0, 1);
        offsets[1] = new int2(1, 1);
        offsets[2] = new int2(1, 0);
        offsets[3] = new int2(1, -1);
        offsets[4] = new int2(0, -1);
        offsets[5] = new int2(-1, -1);
        offsets[6] = new int2(-1, 0);
        offsets[7] = new int2(-1, 1);

        AStar aStar = new AStar
        {
            isObstacle = isObstacle,
            offsets = offsets,
            starts = nativeStarts,
            resultList = resultList,
            end = end,
            safeGuard = safeGuard,
        };

        JobHandle handle = aStar.Schedule(starts.Count, 16);
        handle.Complete();

        NativeKeyValueArrays<int2, Node> keyValueArray = resultList.GetKeyValueArrays(Allocator.Temp);
        Dictionary<int2, Queue<Node>> paths = new Dictionary<int2, Queue<Node>>();

        for (int i = 0; i < keyValueArray.Keys.Length; i++)
        {
            if (!paths.ContainsKey(keyValueArray.Keys[i]))
            {
                paths.Add(keyValueArray.Keys[i], new Queue<Node>());
                paths[keyValueArray.Keys[i]].Enqueue(keyValueArray.Values[i]);
            }
            else
            {
                paths[keyValueArray.Keys[i]].Enqueue(keyValueArray.Values[i]);
            }
        }

        foreach (int2 start in paths.Keys)
        {
            StartCoroutine(MoveUnitCoroutine(start, paths[start]));
        }

        isObstacle.Dispose();
        offsets.Dispose();
        nativeStarts.Dispose();
        resultList.Dispose();
    }

    IEnumerator MoveUnitCoroutine(int2 start, Queue<Node> path)
    {
        Node n = path.Dequeue();

        Vector3 startPos = map.GetCellCenterWorld(new Vector3Int(start.x, start.y, 0));
        GameObject unit = Instantiate(tilePreFab, new Vector3(startPos.x - map.cellGap.x / 2f, startPos.y - map.cellGap.y / 2f, 0), Quaternion.identity);

        while (path.Count != 0)
        {
            Vector3 nextPos = map.GetCellCenterWorld(new Vector3Int(n.coord.x, n.coord.y, 0));
            nextPos.x -= map.cellGap.x / 2f;
            nextPos.y -= map.cellGap.y / 2f;

            while ((nextPos - unit.transform.position).magnitude > 0.1f)
            {
                unit.transform.Translate((nextPos - unit.transform.position).normalized * Time.deltaTime * unitSpeed);

                yield return null;
            }

            n = path.Dequeue();
        }

        Destroy(unit);
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct AStar : IJobParallelFor
    {
        [ReadOnly] public NativeHashMap<int2, bool> isObstacle;
        [ReadOnly] public NativeArray<Node> starts;
        [ReadOnly] public NativeArray<int2> offsets;
        [NativeDisableParallelForRestriction] public NativeMultiHashMap<int2, Node> resultList;

        public Node end;
        public int safeGuard;

        public void Execute(int r)
        {
            NativeHashMap<int2, Node> openSet = new NativeHashMap<int2, Node>(safeGuard, Allocator.Temp);
            NativeHashMap<int2, Node> nodes = new NativeHashMap<int2, Node>(safeGuard, Allocator.Temp);

            Node current = starts[r];

            current.gScore = 0;
            current.hScore = Distance(current.coord, end.coord);
            current.fScore = current.gScore + current.hScore;

            openSet.TryAdd(current.coord, current);

            int counter = 0;

            while (openSet.Count() != 0 && !end.coord.Equals(current.coord))
            {
                Node result = new Node();
                float fScore = float.MaxValue;

                NativeArray<Node> nodeArray = openSet.GetValueArray(Allocator.Temp);

                for (int i = 0; i < nodeArray.Length; i++)
                {
                    if (nodeArray[i].fScore <= fScore)
                    {
                        result = nodeArray[i];
                        fScore = nodeArray[i].fScore;
                    }
                }

                nodeArray.Dispose();

                current = openSet[result.coord];
                openSet.Remove(current.coord);

                for (int i = 0; i < offsets.Length; i++)
                {
                    if (!isObstacle.ContainsKey(current.coord + offsets[i]))
                    {
                        Node neighbour = new Node
                        {
                            coord = current.coord + offsets[i],
                            parent = current.coord,
                            gScore = current.gScore +
                                Distance(current.coord, current.coord + offsets[i]),
                            hScore = Distance(current.coord + offsets[i], end.coord)
                        };

                        neighbour.fScore = neighbour.gScore + neighbour.hScore;

                        if (!nodes.TryAdd(neighbour.coord, neighbour))
                        {
                            if (neighbour.gScore <= nodes[neighbour.coord].gScore)
                            {
                                nodes.Remove(neighbour.coord);
                                nodes.TryAdd(neighbour.coord, neighbour);
                            }
                        }

                        if (neighbour.gScore <= nodes[neighbour.coord].gScore)
                        {
                            openSet.TryAdd(neighbour.coord, neighbour);
                        }
                    }
                }

                counter++;

                if (counter > safeGuard)
                    break;

            }

            //copy solution in result list in case a solution is found
            if (nodes.ContainsKey(end.coord))
            {
                int2 currentCoord = end.coord;
                resultList.Add(starts[r].coord, end);

                while (!currentCoord.Equals(starts[r].coord))
                {
                    currentCoord = nodes[currentCoord].parent;
                    resultList.Add(starts[r].coord, nodes[currentCoord]);
                }
            }

            openSet.Dispose();
            nodes.Dispose();
        }

        public float Distance(int2 coordA, int2 coordB)
        {
            float a = coordB.x - coordA.x;
            float b = coordB.y - coordA.y;

            return Mathf.Sqrt(a * a + b * b);
        }
    }
}
