using BackgroundResourceProcessing.Mathematics;

namespace BackgroundResourceProcessing.Test.Shadow;

using Orbit = BackgroundResourceProcessing.Mathematics.Orbit;

/// <summary>
/// The bodies from the Kcalbeloh planet pack that the regression tests need,
/// recorded from <see cref="SolarSystem.Record"/> in a game with it
/// installed. Kcalbeloh is a black hole orbiting the Sun, with stars orbiting
/// it. The values are exact copies, so the tests don't depend on what is
/// installed.
/// </summary>
internal static class KcalbelohTestSystem
{
    public const int Sun = 0;

    /// <summary>A black hole, treated as a star.</summary>
    public const int Kcalbeloh = 1;

    /// <summary>A star orbiting Kcalbeloh.</summary>
    public const int Sunorc = 2;

    /// <summary>
    /// The barycentre of a binary planet orbiting <see cref="Sunorc"/>. It has
    /// a radius of 10 km, but is invisible and doesn't block light.
    /// </summary>
    public const int SimeSystem = 3;

    /// <summary>One half of the binary planet.</summary>
    public const int Simetra = 4;

    /// <summary>A star orbiting Kcalbeloh.</summary>
    public const int AralcA = 5;

    /// <summary>A star orbiting <see cref="AralcA"/>.</summary>
    public const int AralcB = 6;

    /// <summary>A planet orbiting <see cref="AralcA"/>.</summary>
    public const int Anehta = 7;

    /// <summary>A moon of <see cref="Anehta"/>.</summary>
    public const int Elad = 8;

    /// <summary>The other half of the binary planet.</summary>
    public const int Simeht = 9;

    /// <summary>A small moon orbiting <see cref="SimeSystem"/>.</summary>
    public const int Noira = 10;

    /// <summary>A moon orbiting <see cref="SimeSystem"/>.</summary>
    public const int Norihc = 11;

    /// <summary>A moon orbiting <see cref="SimeSystem"/>.</summary>
    public const int Noyreg = 12;

    public static readonly string[] Names =
    [
        "Sun",
        "Kcalbeloh",
        "Sunorc",
        "SimeSystem",
        "Simetra",
        "AralcA",
        "AralcB",
        "Anehta",
        "Elad",
        "Simeht",
        "Noira",
        "Norihc",
        "Noyreg",
    ];

    /// <summary>
    /// The gravitational parameter of each body.
    /// </summary>
    public static readonly double[] Mu =
    [
        1.1723327948324872E+18,
        7.8641018344000009E+20,
        8.0756368000000013E+17,
        382378065440000.06,
        237917603840000.03,
        2.2024464E+18,
        4.3381520000000013E+17,
        685207771360000.13,
        1935754256799.9998,
        144460461600000,
        5370121540,
        487137885695.99994,
        1372931000000.0005,
    ];

    /// <summary>
    /// Build the body array. A new array is returned each time so tests can't
    /// interfere with each other.
    /// </summary>
    public static SystemBody[] Create() =>
        [
            Root(
                0,
                63245400,
                new Vector3d(-8724633861.9092236, 1014.9098918240052, 10431715643.583284)
            ),
            Orbiting(
                1,
                0,
                2400000000,
                new Vector3d(-0.61237243569579458, -0.61237243569579447, 0.49999999999999994),
                new Vector3d(0.70710678118654746, -0.70710678118654757, 3.0615158845559425E-17),
                new Vector3d(0.35355339059327373, 0.35355339059327368, 0.86602540378443871),
                0.001,
                30,
                271800000000000,
                90,
                135,
                0,
                0,
                2.41630236215822E-13,
                0,
                1.1723327948324872E+18,
                26003307390584.6,
                271799728200000
            ),
            Orbiting(
                2,
                1,
                42753932.5824,
                new Vector3d(-0.49999999999999978, 0.86602540378443871, 0),
                new Vector3d(-0.86483854606689592, -0.49931476737728669, 0.052335956242943835),
                new Vector3d(0.045324267637740147, 0.026167978121471907, 0.99862953475457383),
                0.05,
                3,
                1052000000000,
                0,
                120,
                0,
                1731456396.6839893,
                2.5989681337735135E-08,
                45,
                7.8641018344000009E+20,
                241756919.81482112,
                1049370000000
            ),
            Invisible(
                3,
                2,
                10000,
                new Vector3d(-0.65568978974934633, 0.75446052520985607, -0.02932943092741578),
                new Vector3d(-0.75461064286628865, -0.6561257630800893, -0.0078587973314654478),
                new Vector3d(-0.025172947610170686, 0.016979367557088326, 0.99953890558896052),
                0.05,
                1.74,
                18425000000,
                255,
                236,
                0,
                834916.90580378624,
                3.5931648652956148E-07,
                0.2999994091375493,
                8.0756368000000013E+17,
                17486493.224581443,
                18378937500
            ),
            Orbiting(
                4,
                3,
                4800000,
                new Vector3d(1, -2.4432465485859318E-16, 1.7084844193459934E-17),
                new Vector3d(2.4492127076447545E-16, 0.9975640502598242, -0.0697564737441253),
                new Vector3d(0, 0.0697564737441253, 0.9975640502598242),
                0.003,
                -4,
                10360079.110886149,
                360,
                0,
                0,
                0,
                0.00013617364919136588,
                0,
                382378065440000.06,
                46140.977674394089,
                10359985.870174151
            ),
            Orbiting(
                5,
                1,
                112893150.384,
                new Vector3d(1, 0, 0),
                new Vector3d(0, 0.99939082701909576, -0.034899496702500969),
                new Vector3d(0, 0.034899496702500969, 0.99939082701909576),
                0.02,
                -2,
                2460000000000,
                0,
                0,
                0,
                0,
                7.2681168968680619E-09,
                0,
                7.8641018344000009E+20,
                864486000.4779371,
                2459016000000
            ),
            Orbiting(
                6,
                5,
                22831611.9264,
                new Vector3d(-1, 1.2243377774121748E-16, 2.564622045357502E-18),
                new Vector3d(-1.2246063538223773E-16, -0.9997806834748455, -0.020942419883356957),
                new Vector3d(0, -0.020942419883356957, 0.9997806834748455),
                0.002,
                1.2,
                13166710000,
                180,
                0,
                0,
                0,
                9.82283318240064E-07,
                0,
                2.2024464E+18,
                6396510.24353853,
                13166657333.16
            ),
            Orbiting(
                7,
                5,
                8000000,
                new Vector3d(0.069756473744125233, 0.9975640502598242, 0),
                new Vector3d(-0.99732665702980616, 0.0697398735923731, 0.02181488503456112),
                new Vector3d(0.021761745071029216, -0.0015217294551444732, 0.99976202707990913),
                0.04,
                1.25,
                69524780000,
                0,
                86,
                0,
                1235255.4179118562,
                8.0954918343253663E-08,
                0.100000001490116,
                2.2024464E+18,
                77613385.76784806,
                69413540352
            ),
            Orbiting(
                8,
                7,
                520000,
                new Vector3d(-0.92050464148053068, 0.39073115076841947, 0.00061059969409662357),
                new Vector3d(-0.39073101099132584, -0.92050484110290831, 0.00033846093725205007),
                new Vector3d(0.00069430720589454817, 7.2974627914994708E-05, 0.99999975630607407),
                0.02,
                -0.04,
                142429000,
                241,
                276,
                0,
                324680.72123589483,
                1.5399744034593542E-05,
                5,
                685207771360000.13,
                408005.82743876911,
                142372028.4
            ),
            Orbiting(
                9,
                3,
                4000000,
                new Vector3d(-1, 1.2216232742929659E-16, -8.5424220967299672E-18),
                new Vector3d(-1.2246063538223773E-16, -0.9975640502598242, 0.0697564737441253),
                new Vector3d(0, 0.0697564737441253, 0.9975640502598242),
                0.003,
                -4,
                17062420.889113858,
                180,
                0,
                0,
                0,
                0.00013617364919136588,
                0,
                382378065440000.06,
                46140.977674394089,
                17062267.327325854
            ),
            Orbiting(
                10,
                3,
                74000,
                new Vector3d(-0.86298529634329435, 0.48569741899634244, 0.13912007574598276),
                new Vector3d(0.50337638137056029, 0.85014077832741841, 0.1545084971874737),
                new Vector3d(-0.043227271178699581, 0.20336832153790008, -0.97814760073380569),
                0.03,
                168,
                57982500,
                42,
                192,
                0,
                90314.797081908808,
                4.4289530943332549E-05,
                4,
                382378065440000.06,
                141866.15151148877,
                57930315.75
            ),
            Orbiting(
                11,
                3,
                336000,
                new Vector3d(0.80904597279228152, 0.58764891955775855, 0.010647124080713074),
                new Vector3d(-0.58772016570149144, 0.80871078603929136, 0.023913832221602687),
                new Vector3d(0.0054424935831389916, -0.025604919181878977, 0.99965732497555726),
                0.04,
                1.5,
                124421200,
                24,
                12,
                0,
                56778.652268226062,
                1.4089802558552249E-05,
                0.8,
                382378065440000.06,
                445938.49211647111,
                124222126.08
            ),
            Orbiting(
                12,
                3,
                500000,
                new Vector3d(-0.95104988394947865, -0.30900209788266392, 0.0046713749363240372),
                new Vector3d(0.30902436034105651, -0.95103997206439683, 0.0051880874657408991),
                new Vector3d(0.0028395343780312442, 0.0063776986338234054, 0.99997563070539475),
                0.04,
                0.4,
                215820000,
                42,
                156,
                0,
                243210.49157735167,
                6.1674970938617334E-06,
                1.5,
                382378065440000.06,
                1018757.7248204937,
                215474688
            ),
        ];

    static SystemBody Root(int index, double radius, Vector3d position) =>
        new()
        {
            HasOrbit = false,
            Position = position,
            Index = index,
            Radius = radius,
        };

    static SystemBody Invisible(
        int index,
        int parent,
        double radius,
        Vector3d x,
        Vector3d y,
        Vector3d z,
        double eccentricity,
        double inclination,
        double semiMajorAxis,
        double argumentOfPeriapsis,
        double lan,
        double epoch,
        double obtAtEpoch,
        double meanMotion,
        double meanAnomalyAtEpoch,
        double gravParameter,
        double period,
        double semiLatusRectum
    )
    {
        var body = Orbiting(
            index,
            parent,
            radius,
            x,
            y,
            z,
            eccentricity,
            inclination,
            semiMajorAxis,
            argumentOfPeriapsis,
            lan,
            epoch,
            obtAtEpoch,
            meanMotion,
            meanAnomalyAtEpoch,
            gravParameter,
            period,
            semiLatusRectum
        );
        body.Invisible = true;
        return body;
    }

    static SystemBody Orbiting(
        int index,
        int parent,
        double radius,
        Vector3d x,
        Vector3d y,
        Vector3d z,
        double eccentricity,
        double inclination,
        double semiMajorAxis,
        double argumentOfPeriapsis,
        double lan,
        double epoch,
        double obtAtEpoch,
        double meanMotion,
        double meanAnomalyAtEpoch,
        double gravParameter,
        double period,
        double semiLatusRectum
    ) =>
        new()
        {
            HasOrbit = true,
            Index = index,
            Radius = radius,
            Orbit = new Orbit
            {
                OrbitFrame = new Planetarium.CelestialFrame
                {
                    X = x,
                    Y = y,
                    Z = z,
                },
                Eccentricity = eccentricity,
                Inclination = inclination,
                SemiMajorAxis = semiMajorAxis,
                ArgumentOfPeriapsis = argumentOfPeriapsis,
                LAN = lan,
                Epoch = epoch,
                ObTAtEpoch = obtAtEpoch,
                MeanMotion = meanMotion,
                MeanAnomalyAtEpoch = meanAnomalyAtEpoch,
                GravParameter = gravParameter,
                Period = period,
                ParentBodyIndex = parent,
                SemiLatusRectum = semiLatusRectum,
            },
        };
}
