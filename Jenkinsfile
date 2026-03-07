def notify(status){
	emailext (
		body: '$DEFAULT_CONTENT',
		recipientProviders: [
			[$class: 'CulpritsRecipientProvider'],
			[$class: 'DevelopersRecipientProvider'],
			[$class: 'RequesterRecipientProvider']
		],
		replyTo: '$DEFAULT_REPLYTO',
		subject: '$DEFAULT_SUBJECT',
		to: '$DEFAULT_RECIPIENTS'
	)
}

@NonCPS
def killall_jobs() {
	def jobname = env.JOB_NAME
	def buildnum = env.BUILD_NUMBER.toInteger()
	def killnums = ""
	def job = Jenkins.instance.getItemByFullName(jobname)
	def fixed_job_name = env.JOB_NAME.replace('%2F','/')

	for (build in job.builds) {
		if (!build.isBuilding()) { continue; }
		if (buildnum == build.getNumber().toInteger()) { continue; println "equals" }
		if (buildnum < build.getNumber().toInteger()) { continue; println "newer" }

		echo "Kill task = ${build}"

		killnums += "#" + build.getNumber().toInteger() + ", "

		build.doStop();
	}

	if (killnums != "") {
		notify("Killing task(s) ${fixed_job_name} ${killnums} in favor of #${buildnum}, ignore following failed builds for ${killnums}");
	}
	echo "Done killing"
}

def buildStep(DOCKER_ROOT, DOCKERIMAGE, DOCKERTAG, DOCKERFILE, BUILD_NEXT) {
	def fixed_job_name = env.JOB_NAME.replace('%2F','/')
	def tag = ''
	try {
		checkout scm;

		def buildenv = '';
		def publish = false;
		if (env.BRANCH_NAME.equals('master')) {
			buildenv = 'production';
			tag = "${DOCKERTAG}";
			publish = true;
		} else if (env.BRANCH_NAME.equals('dev')) {
			buildenv = 'development';
			tag = "${DOCKERTAG}-dev";
			publish = true;
		} else {
			tag = "${env.BRANCH_NAME.replace('/','-')}";
		}

		docker.withRegistry("https://index.docker.io/v1/", "dockerhub") {
			def customImage
			stage("Building ${DOCKERIMAGE}:${tag}...") {
				customImage = docker.build("${DOCKER_ROOT}/${DOCKERIMAGE}:${tag}", "--build-arg BUILDENV=${buildenv} --network=host --pull -f ${DOCKERFILE} .");
			}

			stage("Testing ${DOCKERIMAGE}:${tag}...") {
            	def testImage = docker.build("${DOCKER_ROOT}/${DOCKERIMAGE}:${tag}_test", "--build-arg BUILDENV=${buildenv} --network=host --pull --target setup -f ${DOCKERFILE} .");
            	testImage.inside("-u 0") {
					try{
						sh("dotnet test --logger \"trx;LogFileName=../../Testing/unit_tests.xml\"");
						sh("dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover");
						sh("chmod 777 -R .");
					} catch(err) {
						currentBuild.result = 'FAILURE'
						sh("chmod 777 -R .");
						notify('Testing failed')
					}

					archiveArtifacts (
						artifacts: 'Testing/**.xml',
						fingerprint: true
					)

                    withCredentials([string(credentialsId: 'MBEIJER_CSHARP_DNS_SERVER_CODECOV_TOKEN', variable: 'CODECOV_TOKEN')]) {
                        sh('''#!/usr/bin/env bash
set -euo pipefail

curl -Os https://uploader.codecov.io/latest/linux/codecov
chmod +x codecov

# Jenkins workspaces may be owned by a different uid when running in Docker.
git config --global --add safe.directory "$PWD" || true

GIT_SHA="${GIT_COMMIT:-$(git rev-parse HEAD)}"
GIT_BRANCH="${BRANCH_NAME:-${GIT_BRANCH:-$(git rev-parse --abbrev-ref HEAD)}}"
GIT_SLUG="mbeijer/csharp-dns-server"
BUILD_URL_VALUE="${BUILD_URL:-}"

./codecov \
	--token "$CODECOV_TOKEN" \
	--file "Testing/unit_tests.xml" \
	--file "Dns.UnitTests/coverage.opencover.xml" \
	--sha "$GIT_SHA" \
	--branch "$GIT_BRANCH" \
	--slug "$GIT_SLUG" \
	--build "$BUILD_NUMBER" \
	--build-url "$BUILD_URL_VALUE" \
	--name "jenkins-${JOB_NAME}-${BUILD_NUMBER}" \
	--disable-search
''')
                    }

					stage("Xunit") {
						xunit (
							testTimeMargin: '3000',
							thresholdMode: 1,
							thresholds: [
								skipped(failureThreshold: '1000'),
								failed(failureThreshold: '0')
							],
							tools: [MSTest(
								pattern: 'Testing/**.xml',
								deleteOutputFiles: true,
								failIfNotNew: false,
								skipNoTestFiles: true,
								stopProcessingIfError: true
							)],
							skipPublishingChecks: false
						);
					}
            	}
            }

			if (publish) {
				stage("Pushing to docker hub registry...") {
					customImage.push();
				}
			}
		}

		if (!BUILD_NEXT.equals('')) {
			build "${BUILD_NEXT}/${env.BRANCH_NAME}";
		}
	} catch(err) {
		currentBuild.result = 'FAILURE'
		notify("Build Failed: ${fixed_job_name} #${env.BUILD_NUMBER} Target: ${DOCKER_ROOT}/${DOCKERIMAGE}:${tag}")
		throw err
	}
}

node('master') {
	killall_jobs();
	def fixed_job_name = env.JOB_NAME.replace('%2F','/');

	checkout scm;

	def branches = [:]
	def project = readJSON file: "JenkinsEnv.json";

	project.builds.each { v ->
		branches["Build ${v.DockerRoot}/${v.DockerImage}:${v.DockerTag}"] = {
			node("amd64") {
				buildStep(v.DockerRoot, v.DockerImage, v.DockerTag, v.Dockerfile, v.BuildIfSuccessful)
			}
		}
	}

	sh "rm -rf ./*"

	parallel branches;
}