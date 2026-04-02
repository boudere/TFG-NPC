using System.Collections.Generic;
using UnityEngine;

public class ZoneManager : MonoBehaviour
{
    public static ZoneManager instance;

    //public FranjaTrigger[] franjas;
    //public SideTrigger[] sides;

    public GameObject[] franjas;
    public GameObject[] sides;

    public int totalFranjas = 5;
    public int totalSides = 2;

    public struct ZonaBola
    {
        public int zona;
        public int team;
    }

    ZonaBola franjaBola;
    ZonaBola sideBola;

    List<ZonaBola> zonas = new List<ZonaBola>();

    private void Awake()
    {
        franjas = GameObject.FindGameObjectsWithTag("Franja");
        sides = GameObject.FindGameObjectsWithTag("Side");
        instance = this;
    }



    public List<ZonaBola> whereIsBall()
    {
    

        zonas.Clear();
      
        foreach (GameObject f in franjas)
        {
            FranjaTrigger franja = f.GetComponent<FranjaTrigger>();
            if (franja != null && franja.IsBallInside())
            {
                ZonaBola zona = new ZonaBola();
                zona.zona = franja.franjaIndex;
                zona.team = franja.team;
                zonas.Add(zona);
                break;
            }
        }

        //foreach (GameObject s in sides)
        //{
        //    SideTrigger side = s.GetComponent<SideTrigger>();
        //    if (side != null && side.IsBallInside())
        //    {
        //        ZonaBola zona = new ZonaBola();
        //        zona.zona = side.sideIndex;
        //        zona.team = side.team;
        //        Debug.Log("BBS");
        //        zonas.Add(zona);
        //        break;
        //    }
        //}
      
        return zonas;
    }

    public List<ZonaBola> getAreasAdyacentes()
    {
        List<ZonaBola> resultado = new List<ZonaBola>();
        List<ZonaBola> zonas = whereIsBall();

        if (zonas == null || zonas.Count == 0)
            return resultado;

        ZonaBola zonaActual = zonas[0];

        ZonaBola actual = new ZonaBola();
        actual.zona = zonaActual.zona;
        actual.team = zonaActual.team;
        resultado.Add(actual);

        if (zonaActual.zona < 4)
        {
            ZonaBola superior = new ZonaBola();
            superior.zona = zonaActual.zona + 1;
            superior.team = zonaActual.team;
            resultado.Add(superior);
        }

        if (zonaActual.zona > 0)
        {
            ZonaBola inferior = new ZonaBola();
            inferior.zona = zonaActual.zona - 1;
            inferior.team = zonaActual.team;
            resultado.Add(inferior);
        }
        else
        {
            ZonaBola otroCero = new ZonaBola();
            otroCero.zona = 0;
            otroCero.team = (zonaActual.team == 0) ? 1 : 0;
            resultado.Add(otroCero);
        }

        return resultado;
    }

    public Vector3 GetRandomPointInSelectedAreas(List<ZonaBola> areas, float y)
    {
        List<FranjaTrigger> franjasValidas = new List<FranjaTrigger>();

        foreach (GameObject franja in franjas)
        {
            FranjaTrigger f = franja.GetComponent<FranjaTrigger>();
            if (f == null) continue;

            foreach (ZonaBola area in areas)
            {
                if (f.franjaIndex == area.zona && f.team == area.team)
                {
                    franjasValidas.Add(f);
                    break;
                }
            }
        }

        if (franjasValidas.Count == 0)
            return Vector3.zero;

        FranjaTrigger franjaElegida = franjasValidas[Random.Range(0, franjasValidas.Count)];

        Collider col = franjaElegida.GetComponent<Collider>();
        if (col == null)
            return franjaElegida.transform.position;

        Bounds b = col.bounds;

        float x = Random.Range(b.min.x, b.max.x);
        float z = Random.Range(b.min.z, b.max.z);

        return new Vector3(x, y, z);
    }

    public List<ZonaBola> getAreasByTeam(int teamId)
    {
        List<ZonaBola> resultado = new List<ZonaBola>();

        foreach (GameObject franja in franjas)
        {
            FranjaTrigger f = franja.GetComponent<FranjaTrigger>();
            if (f == null) continue;

            if (f.team == teamId)
            {
                ZonaBola zona = new ZonaBola();
                zona.zona = f.franjaIndex;
                zona.team = f.team;
                resultado.Add(zona);
            }
        }

        return resultado;
    }

}