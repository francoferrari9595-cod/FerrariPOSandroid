# Corrección de compilación Android - Ferrari POS Manager

El error mostrado en GitHub incluía Gradle 9.7.1. El proyecto usa Android Gradle Plugin 8.9.3, por lo que se fija el wrapper a Gradle 8.11.1 y el workflow ejecuta exclusivamente `./gradlew` del proyecto.

La advertencia `multi-string notation ... lint-gradle` pertenece al tooling interno de Android Gradle Plugin y no es la causa primaria del fallo. El workflow ahora imprime `./gradlew --version` antes de compilar para comprobar que GitHub no esté usando un Gradle global diferente.
