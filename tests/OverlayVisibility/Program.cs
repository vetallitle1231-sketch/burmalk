using System;
using Burmalk.Services;

var state=new OverlayVisibility();
int passed=0;
void Check(bool value,string label){if(!value)throw new Exception(label);passed++;}
Check(!state.IsVisible(true,false,true,false),"No automatic overlay without a detected game");
Check(state.Toggle(true,false,true,false),"First F8 shows overlay in an unknown game even when Enable overlay was already checked");
Check(state.IsVisible(true,false,true,false),"Manual overlay remains visible without detection");
Check(!state.Toggle(true,false,true,false),"Second F8 hides it");
Check(!state.IsVisible(false,true,true,false),"Hidden manual overlay stays hidden when game detection begins");
state.Reset();
Check(state.IsVisible(true,true,true,false),"Recognized games still show automatically");
Check(!state.IsVisible(true,true,false,false),"Disabled game profile blocks automatic display");
Check(state.Toggle(true,true,false,false),"Explicit F8 overrides an automatic profile restriction");
state.Reset();
Check(!state.IsVisible(false,true,true,false),"Master switch disables automatic display");
Check(state.IsVisible(false,false,false,true),"Preview is available without a game");
Check(!state.Toggle(false,false,false,true),"F8 hides an active preview");
Check(!state.IsVisible(false,false,false,false),"Hidden preview does not turn into a manual overlay");
state.Reset();
Check(!state.IsVisible(true,false,true,false),"Applying settings restores automatic mode");
Console.WriteLine($"PASS: {passed} overlay scenarios; no windows, keyboard hooks or game access.");
