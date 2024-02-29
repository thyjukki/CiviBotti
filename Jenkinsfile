def app = null
pipeline {
    agent { label 'linux&&docker' }
    options {
        timestamps()
        disableConcurrentBuilds()
        ansiColor('xterm')
    }
    environment {
        HOME='/tmp/home'
        DOTNET_CLI_TELEMETRY_OPTOUT=1
    }
    stages {
        stage('SonarQube') {
            agent {
                docker {
                    image 'ghcr.io/nosinovacao/dotnet-sonar:latest8'
                }
            }
            steps {
                withSonarQubeEnv('SonarQube Jukki') {
                    sh '''dotnet /sonar-scanner/SonarScanner.MSBuild.dll begin \
                 /k:"civibotti" \
                 /n:"civibotti"'''
                    sh 'dotnet build "CiviBotti/CiviBotti.csproj" -c Release'
                    //sh 'dotnet test --collect:"XPlat Code Coverage"'
                    sh 'dotnet /sonar-scanner/SonarScanner.MSBuild.dll end'
                }
            }
        }
        stage("Quality Gate") {
            steps {
                timeout(time: 30, unit: 'MINUTES') {
                    waitForQualityGate abortPipeline: true
                }
            }
        }
        stage("Build") {
            steps {
                script {
                    app = docker.build("jukki/civibotti")
                }
            }
        }
        stage("Docker push") {
            when {
                branch 'master'
            }
            steps {
                script {
                    docker.withRegistry('https://nexus.jukk.it', 'nexus-jenkins-user' ) {
                        app.push("0.${BUILD_NUMBER}")
                        app.push("latest")
                    }
                }
            }
        }
        stage('Deploy App') {
            when {
                branch 'master'
            }
            agent {
                docker {
                    image 'caprover/cli-caprover'
                    label 'linux&&docker'
                }
            }
            steps {
                withCredentials([string(credentialsId: 'caprover-password', variable: 'CAPROVER_PASSWORD')]) {
                    sh "caprover deploy -c captain-definition"
                }
            }
        }
    }
}
