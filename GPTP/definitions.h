#pragma once

//Helper macro for converting #defined constants to C-strings
#define STR_HELPER(x) #x
#define STR(x) STR_HELPER(x)

//Display a user created message (contained in x) of warning
//at compilation
#define USER_WARNING(x) __pragma(message(__FILE__"("STR(__LINE__)") : warning: "#x))

//if USER_WARNING(x) doesn't work, then it may work if you 
//put the following line where you want to create the warning:
//#pragma message (__FILE__"("STR(__LINE__)") : warning: YOUR_MESSAGE")
//If you get an error about STR, then this file is not included and you
//may want to either include it or add the other 2 macros directly into
//the file where you need this trick

//Min-max macros to not rely on std
#define MIN(a,b) ((a<=b) ? a : b)
#define MAX(a,b) ((a>=b) ? a : b)

//Helper macro for retrieving the size of an array
//Taken from http://blogs.msdn.com/b/the1/archive/2004/05/07/128242.aspx
template <typename T, size_t N>
char ( &_ArraySizeHelper( T (&array)[N] ))[N];
#define countof( array ) (sizeof( _ArraySizeHelper( array ) ))

//What is the plugin's ID?
//Plugin ID generated by MakeID.HTA (based on CRC32)
#define PLUGIN_ID 0x1B4D69B6

//What is the plugin's name (and version)?
#define PLUGIN_NAME "Starcraft: Manifold"

//If defined, will enable the events system (not native of SC)
//#define EVENTS_SYSTEM
