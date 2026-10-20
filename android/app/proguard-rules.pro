# kotlinx.serialization: generierte Serializer der Datenklassen behalten.
-keepattributes *Annotation*, InnerClasses
-keepclassmembers class de.estrichone.core.** {
    *** Companion;
}
-keepclasseswithmembers class de.estrichone.core.** {
    kotlinx.serialization.KSerializer serializer(...);
}
-keep,includedescriptorclasses class de.estrichone.core.**$$serializer { *; }
-dontwarn org.slf4j.**
