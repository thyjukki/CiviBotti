﻿using System.ComponentModel;

namespace CiviBotti
{
    public enum Command
    {
        [Description("newgame")]
        Newgame,
        [Description("register")]
        Register,
        [Description("addgame")]
        Addgame,
        [Description("removegame")]
        Removegame,
        [Description("order")]
        Order,
        [Description("next")]
        Next,
        [Description("autocracy")]
        Autocracy,
        [Description("freedom")]
        Freedom,
        [Description("oispa")]
        Oispa,
        [Description("teekari")]
        Teekari,
        [Description("tee")]
        Tee,
        [Description("eta")]
        Eta,
        [Description("help")]
        Help,
        [Description("turntimer")]
        Turntimer,
        [Description("turntimers")]
        Turntimers,
        [Description("listsubs")]
        Listsubs,
        [Description("addsub")]
        Addsub,
        [Description("removesub")]
        Removesub,
        [Description("doturn")]
        Doturn,
        [Description("submitturn")]
        Submitturn
    }
}