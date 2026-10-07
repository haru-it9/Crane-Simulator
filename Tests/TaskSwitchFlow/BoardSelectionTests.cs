using System;
using System.Collections.Generic;
using UnityEngine;
static class BoardSelectionTests
{
    static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    static GameObject Board(float y,float weight)
    {
        var b=new GameObject();b.transform.position=new Vector3(16,y,2);b.components[typeof(BoardInfo)]=new BoardInfo {Weight=weight};return b;
    }
    static void Main()
    {
        var g=new BoardGenerator();g.spawnPositions.Add(new Vector3(16,0,2));
        g.generatedBoardsBySpawnIndex[0]=new List<GameObject> {Board(1,100),Board(3,300),Board(2,200)};
        List<GameObject> boards;float weight;int spawn,count;
        Require(g.TryGetPickupBoards(16,2,2,out boards,out weight,out spawn) && weight==500 && boards[0].transform.position.y==3 && boards[1].transform.position.y==2,"exact top-board count/weight selection failed");
        Require(g.TryGetPickupTargetWeightKg(16,2,out weight,out count,out spawn) && count==1 && weight==300,"legacy default selection changed");
        Require(!g.TryGetPickupBoards(16,2,4,out boards,out weight,out spawn),"insufficient boards accepted");
        Require(!g.TryGetPickupBoards(99,2,1,out boards,out weight,out spawn),"unmatched coordinates accepted");
        g.generatedBoardsBySpawnIndex[0][1].transform.position=new Vector3(16,3,8);
        Require(g.TryGetPickupBoards(16,2,2,out boards,out weight,out spawn) && weight==300,"moved boards still selected");
        g.generatedBoardsBySpawnIndex[0][2].components.Clear();
        Require(!g.TryGetPickupBoards(16,2,2,out boards,out weight,out spawn),"missing actual board weight accepted");
        Console.WriteLine("PASS: production BoardGenerator selection methods, top-down exact counts/weights, legacy count, missing boards and moved boards");
    }
}
